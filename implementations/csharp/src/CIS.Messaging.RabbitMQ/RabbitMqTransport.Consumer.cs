using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace CIS.Messaging.RabbitMQ;

public sealed partial class RabbitMqTransport
{
    private sealed class ConsumerOwner
    {
        private readonly object gate = new();
        private readonly RabbitMqTransport transport;
        private readonly IChannel channel;
        private readonly string alias;
        private readonly string route;
        private readonly bool reply;
        private readonly Func<IMessageDelivery, CancellationToken, Task> dispatch;
        private readonly SemaphoreSlim nativeGate = new(1, 1);
        private readonly TaskCompletionSource initialRegistration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? tag;
        private Task? stop;
        private TaskCompletionSource? drained;
        private int active;
        private int stopped;
        private int registered;

        internal ConsumerOwner(RabbitMqTransport transport, IChannel channel, string alias, string route, bool reply,
            Func<IMessageDelivery, CancellationToken, Task> dispatch)
        {
            this.transport = transport;
            this.channel = channel;
            this.alias = alias;
            this.route = route;
            this.reply = reply;
            this.dispatch = dispatch;
        }

        internal bool IsStopped => Volatile.Read(ref stopped) != 0;
        internal bool IsReady => !IsStopped && Volatile.Read(ref registered) != 0 && channel.IsOpen;

        internal void SetRegistered(bool value)
        {
            Volatile.Write(ref registered, value ? 1 : 0);
            if (value)
            {
                initialRegistration.TrySetResult();
                // A consume confirmation already belongs to the reauthenticated native connection.
                // Make its profile available before that connection can dispatch recovered messages.
                transport.ConfirmNativeRegistration();
            }
        }

        internal Task WaitForRegistrationAsync(CancellationToken cancellationToken)
            => initialRegistration.Task.WaitAsync(cancellationToken);

        internal void CancelFromBroker()
        {
            if (!IsStopped)
            {
                RequestStop("messaging.consumer.cancelled");
                _ = StopAsync();
            }
        }

        internal void SetTag(string value)
        {
            lock (gate)
            {
                tag = value;
            }
        }

        internal async Task ReceiveAsync(object sender, BasicDeliverEventArgs args)
        {
            lock (gate)
            {
                if (IsStopped)
                {
                    return;
                }
                active++;
            }
            await using var delivery = new NativeDelivery(this, args);
            try
            {
                await transport.dispatchReady.Task.ConfigureAwait(false);
                await dispatch(delivery, transport.lifetime.Token).ConfigureAwait(false);
                if (!delivery.IsSettled && !IsStopped)
                {
                    RequestStop("messaging.delivery.unsettled");
                }
            }
            catch (OperationCanceledException) when (transport.lifetime.IsCancellationRequested)
            {
                RequestStop("messaging.consumer.stopping");
            }
            catch (DeliveryConnectionLostException)
            {
                // The old channel cannot settle this delivery. Native connection/topology recovery
                // owns redelivery; closing this consumer here would remove it from that recovery.
                if (!transport.IsReady)
                {
                    transport.SetFailure("messaging.delivery.connection-lost");
                }
            }
            catch (Exception error)
            {
                transport.RecordCallbackFault(error);
                RequestStop("messaging.dispatch.failed");
                throw;
            }
            finally
            {
                lock (gate)
                {
                    active--;
                    if (active == 0)
                    {
                        drained?.TrySetResult();
                    }
                }
                if (IsStopped)
                {
                    _ = StopAsync();
                }
            }
        }

        internal void RequestStop(string code)
        {
            RabbitMqText.Name(code, nameof(code), 128);
            Interlocked.Exchange(ref stopped, 1);
            transport.SetFailure(code);
        }

        internal Task StopAsync()
        {
            lock (gate)
            {
                Interlocked.Exchange(ref stopped, 1);
                return stop ??= Task.Run(StopCoreAsync);
            }
        }

        private async Task StopCoreAsync()
        {
            string? consumerTag;
            lock (gate)
            {
                consumerTag = tag;
            }
            try
            {
                if (consumerTag is not null && channel.IsOpen)
                {
                    await channel.BasicCancelAsync(consumerTag).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is AlreadyClosedException or OperationInterruptedException)
            {
                // A closed native channel already stops admission. Actual disposal below remains awaited.
            }
            Task callbacks;
            lock (gate)
            {
                callbacks = active == 0 ? Task.CompletedTask
                    : (drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
            await callbacks.ConfigureAwait(false);
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        private MessageDeliveryMetadata Metadata(BasicDeliverEventArgs args)
        {
            try
            {
                IDictionary<string, object?>? headers = args.BasicProperties.Headers;
                if (headers?.Count > 16)
                {
                    return InvalidMetadata();
                }
                bool targeted = args.Exchange == RabbitMqTopology.RetryExchange(transport.settings, transport.own, alias);
                bool initial = args.Exchange == RabbitMqTopology.RouteExchange(transport.settings, route);
                bool replyIngress = reply && args.Exchange == RabbitMqTopology.ReplyExchange(transport.settings, transport.own);
                if (!initial && !targeted && !replyIngress)
                {
                    return InvalidMetadata();
                }
                int ordinal = ReadInteger(headers, "cis-ordinal", 1);
                int generation = ReadInteger(headers, "cis-generation", 0);
                int purposeValue = ReadInteger(headers, "cis-purpose", (int)MessageRecordPurpose.Application);
                MessageRecordPurpose purpose = (MessageRecordPurpose)purposeValue;
                string? recordText = ReadText(headers, "cis-outbox", 36);
                Guid? record = recordText is null ? null : Guid.ParseExact(recordText, "D");
                string? target = ReadText(headers, "cis-target", 64);
                string? code = ReadText(headers, "cis-code", 128);
                if (ordinal is < 1 or > 32 || generation < 0 || !Enum.IsDefined(purpose)
                    || targeted && purpose is not MessageRecordPurpose.Retry and not MessageRecordPurpose.Redrive
                    || targeted && (target != alias || record is null || record == Guid.Empty)
                    || !replyIngress && !targeted && purpose != MessageRecordPurpose.Application)
                {
                    return InvalidMetadata();
                }
                return new(route, replyIngress ? MessageRecordPurpose.Reply : purpose, ordinal, record, generation, target, code);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            {
                return InvalidMetadata();
            }
        }

        private MessageDeliveryMetadata InvalidMetadata() => new(route, (MessageRecordPurpose)(-1), 0);

        private static int ReadInteger(IDictionary<string, object?>? headers, string name, int fallback)
        {
            if (headers is null || !headers.TryGetValue(name, out object? value))
            {
                return fallback;
            }
            return value switch
            {
                int selected => selected,
                long selected when selected is >= int.MinValue and <= int.MaxValue => (int)selected,
                short selected => selected,
                byte selected => selected,
                _ => throw new FormatException("Invalid bounded delivery ordinal.")
            };
        }

        private static string? ReadText(IDictionary<string, object?>? headers, string name, int maximum)
        {
            if (headers is null || !headers.TryGetValue(name, out object? value))
            {
                return null;
            }
            string text = value switch
            {
                string selected => selected,
                byte[] selected when selected.Length <= maximum => new UTF8Encoding(false, true).GetString(selected),
                _ => throw new FormatException("Invalid bounded native metadata.")
            };
            return RabbitMqText.Text(text, name, maximum);
        }

        private sealed class NativeDelivery : IMessageDelivery
        {
            private readonly ConsumerOwner owner;
            private readonly BasicDeliverEventArgs args;
            private readonly long generation;
            private readonly object gate = new();
            private RabbitMqEvidenceRegistry.IssuedEvidence? issued;
            private bool evidenceRead;
            private bool disposed;
            private int settled;

            internal NativeDelivery(ConsumerOwner owner, BasicDeliverEventArgs args)
            {
                this.owner = owner;
                this.args = args;
                generation = Volatile.Read(ref owner.transport.connectionGeneration);
                Metadata = owner.Metadata(args);
            }

            public ReadOnlyMemory<byte> Body => args.Body;
            public string SubscriptionAlias => owner.alias;
            public MessageDeliveryMetadata Metadata { get; }
            internal bool IsSettled => Volatile.Read(ref settled) != 0;

            public ValueTask<MessageNativeEvidence?> GetNativeEvidenceAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (!evidenceRead)
                    {
                        issued = owner.transport.evidence.Issue(args.BasicProperties.UserId);
                        evidenceRead = true;
                    }
                    return ValueTask.FromResult(issued?.Evidence);
                }
            }

            public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
                => CompleteAsync(false, false, cancellationToken);

            public Task RequeueAsync(CancellationToken cancellationToken = default)
                => CompleteAsync(true, true, cancellationToken);

            public async Task<bool> QuarantineMalformedAsync(string safeCode, CancellationToken cancellationToken = default)
            {
                RabbitMqText.Name(safeCode, nameof(safeCode), 128);
                if (!owner.transport.settings.SafeDeadLetteringVerified)
                {
                    RequestConsumerStop("messaging.quarantine.unavailable");
                    return false;
                }
                await CompleteAsync(true, false, cancellationToken).ConfigureAwait(false);
                return true;
            }

            public void RequestConsumerStop(string safeCode) => owner.RequestStop(safeCode);

            private async Task CompleteAsync(bool negative, bool requeue, CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (Interlocked.CompareExchange(ref settled, 1, 0) != 0)
                {
                    throw new InvalidOperationException("Settle a native delivery once.");
                }
                try
                {
                    await owner.nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (HasLostConnection())
                        {
                            throw new DeliveryConnectionLostException();
                        }
                        if (negative)
                        {
                            await owner.channel.BasicNackAsync(args.DeliveryTag, false, requeue, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await owner.channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        owner.nativeGate.Release();
                    }
                }
                catch (AlreadyClosedException error) when (HasLostConnection())
                {
                    throw new DeliveryConnectionLostException(error);
                }
                catch (DeliveryConnectionLostException)
                {
                    throw;
                }
                catch
                {
                    owner.RequestStop("messaging.delivery.settlement-unavailable");
                    throw;
                }
            }

            private bool HasLostConnection() => generation != Volatile.Read(ref owner.transport.connectionGeneration)
                || owner.transport.connection?.IsOpen != true;

            public ValueTask DisposeAsync()
            {
                lock (gate)
                {
                    if (!disposed)
                    {
                        disposed = true;
                        issued?.Dispose();
                    }
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    // UnregisteredAsync also fires during channel shutdown in Client 7.2.2. Only the
    // actual basic.cancel paths mean cancellation; a shutdown leaves native recovery in charge.
    private sealed class NativeConsumer(IChannel channel, ConsumerOwner owner) : AsyncEventingBasicConsumer(channel)
    {
        public override async Task HandleBasicConsumeOkAsync(string consumerTag, CancellationToken cancellationToken = default)
        {
            await base.HandleBasicConsumeOkAsync(consumerTag, cancellationToken).ConfigureAwait(false);
            owner.SetRegistered(true);
        }

        public override Task HandleChannelShutdownAsync(object channel, ShutdownEventArgs reason)
        {
            owner.SetRegistered(false);
            return base.HandleChannelShutdownAsync(channel, reason);
        }

        public override Task HandleBasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default)
        {
            owner.CancelFromBroker();
            return base.HandleBasicCancelAsync(consumerTag, cancellationToken);
        }

        public override Task HandleBasicCancelOkAsync(string consumerTag, CancellationToken cancellationToken = default)
        {
            owner.CancelFromBroker();
            return base.HandleBasicCancelOkAsync(consumerTag, cancellationToken);
        }
    }

    private sealed class DeliveryConnectionLostException(Exception? inner = null)
        : IOException("The delivery's original native connection can no longer settle its acknowledgement.", inner)
    {
    }
}
