using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckHttpTransportTests
{
    // This fixture owns application state and invokes the shipping middleware.
    // It adapts only request/response containers, never route or health decisions.
    private sealed class Pipeline : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly HealthcheckOptions options;
        private readonly HealthcheckService service;
        private readonly List<IConditionMonitor> monitors = [];
        private readonly IConditionMonitor monitor;
        private RequestDelegate request = null!;
        private readonly HealthPipeServer? pipe;
        private readonly ObservingSnapshotSource source;
        private int authorizationCalls, businessCalls;
        internal string? PipeName { get; }

        private Pipeline(string mode)
        {
            options = new HealthcheckOptions
            {
                DetailsEnabled = mode != "details-disabled",
                MaxReportBytes = mode == "overflow" ? 1024 : 4194304,
                PipeEnabled = mode == "parity",
                PipeName = mode == "parity" ? TransportTestSupport.NewPipeName() : null,
                PipeRequestTimeoutMs = mode == "parity" ? 15000 : 1000,
            };
            services = new ServiceCollection().BuildServiceProvider();
            service = new HealthcheckService(options, new HealthApplicationIdentity
            {
                Name = "transport-fixture",
                InstanceId = Guid.Parse("02fc362c-a583-48c3-8f17-2e5743312e54")
            });
            monitor = service.CreateCondition(new ConditionMonitorOptions
            {
                Name = "critical",
                IsCritical = true,
                InitiallyFlagged = mode == "unhealthy"
            });
            monitors.Add(monitor);
            if (mode == "overflow")
            {
                for (int index = 0; index < 32; index++)
                {
                    monitors.Add(service.CreateCondition(new ConditionMonitorOptions
                    {
                        Name = new string('x', 128),
                        IsCritical = false,
                        InitiallyFlagged = false
                    }));
                }
            }
            if (mode != "not-ready") { service.MarkStartupCompleted(); }
            if (mode == "stopping") { service.BeginStopping(); }
            HealthSnapshot? frozen = mode == "parity" ? service.CaptureSnapshot() : null;
            int captures = 0;
            source = new ObservingSnapshotSource(() =>
            {
                if (mode == "source-throw") { throw new InvalidOperationException("secret-snapshot-error"); }
                if (mode == "source-cancel") { throw new OperationCanceledException("secret-source-cancellation"); }
                HealthSnapshot snapshot = frozen ?? service.CaptureSnapshot();
                if (mode == "alternating")
                {
                    if ((++captures & 1) == 1) { monitor.Flag(); } else { monitor.Clear(); }
                }
                return snapshot;
            });
            PipeName = options.PipeName;
            if (options.PipeEnabled) { pipe = new HealthPipeServer(source, options); }
        }

        private RequestDelegate BuildPipeline()
        {
            IApplicationBuilder app = new ApplicationBuilder(services);
            InstallValidHealthcheck(app, source, options, context =>
            {
                authorizationCalls++;
                string selected = context.Request.Headers["X-Auth"].ToString();
                if (selected == "throw") { throw new InvalidOperationException("secret-authorization-error"); }
                if (selected == "cancel") { throw new OperationCanceledException("secret-authorization-cancellation"); }
                return ValueTask.FromResult(selected != "deny");
            });
            app.Run(async context =>
            {
                businessCalls++;
                await context.Response.WriteAsync("business:" + context.Request.Path);
            });
            return app.Build();
        }

        internal static async Task<Pipeline> Start(string mode = "normal")
        {
            var pipeline = new Pipeline(mode);
            try
            {
                pipeline.request = pipeline.BuildPipeline();
                if (pipeline.pipe is not null)
                {
                    await TransportTestSupport.StartServerAsync(pipeline.pipe, pipeline.PipeName!);
                }
                return pipeline;
            }
            catch (Exception setupError)
            {
                try { await pipeline.DisposeAsync(); }
                catch (Exception cleanupError) { throw new AggregateException("Pipeline setup and actual cleanup both failed.", setupError, cleanupError); }
                throw;
            }
        }

        internal Task<JsonObject> Command(string command = "state")
        {
            switch (command)
            {
                case "state": break;
                case "flag": monitor.Flag(); break;
                case "clear": monitor.Clear(); break;
                case "startup": service.MarkStartupCompleted(); break;
                case "begin-stopping": service.BeginStopping(); break;
                default: throw new ArgumentException("Unknown fixture command.", nameof(command));
            }
            return Task.FromResult(new JsonObject
            {
                ["captures"] = source.Captures,
                ["authorizationCalls"] = authorizationCalls,
                ["businessCalls"] = businessCalls,
                ["revision"] = service.CaptureSnapshot().Revision.ToString(CultureInfo.InvariantCulture)
            });
        }

        internal async Task<HttpResponseMessage> GetAsync(string path)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, path);
            return await SendAsync(message);
        }

        internal async Task<string> GetStringAsync(string path)
        {
            using HttpResponseMessage response = await GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken = default)
        {
            var uri = new Uri(new Uri("http://fixture.local"), message.RequestUri!);
            var context = new DefaultHttpContext();
            var lifetime = new ObservingRequestLifetime { RequestAborted = cancellationToken };
            context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
            context.Request.Path = uri.AbsolutePath;
            context.Request.QueryString = new QueryString(uri.Query);
            context.Request.Method = message.Method.Method;
            foreach (var header in message.Headers) { context.Request.Headers[header.Key] = header.Value.ToArray(); }
            using var body = new MemoryStream();
            context.Response.Body = body;
            await request(context).WaitAsync(TransportTestSupport.Limit);
            if (lifetime.Aborted) { throw new OperationCanceledException("The shipping middleware aborted this request.", cancellationToken); }
            var response = new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode)
            {
                Content = new ByteArrayContent(body.ToArray())
            };
            foreach (var header in context.Response.Headers)
            {
                if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                {
                    response.Content.Headers.Remove(header.Key);
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
            return response;
        }

        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            if (pipe is not null)
            {
                try { await new TransportTestSupport.PipeServerCleanup(pipe).DisposeAsync(); }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (IConditionMonitor owned in monitors)
            {
                try { await owned.DisposeAsync().AsTask().WaitAsync(TransportTestSupport.Limit); }
                catch (Exception error) { errors.Add(error); }
            }
            try { await service.DisposeAsync().AsTask().WaitAsync(TransportTestSupport.Limit); }
            catch (Exception error) { errors.Add(error); }
            try { services.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) { throw new AggregateException("The actual pipeline fixture owners did not all settle successfully.", errors); }
        }
    }
}
