using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;

namespace CIS.Management.Service.Security;

/// <summary>Owns only this reference provider's native metadata HTTP requests through their body settlement.</summary>
internal sealed class ManagementEntraMetadata : IDocumentRetriever, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly HttpClient client;
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration stopRegistration;
    private readonly Lazy<Task> disposal;
    private int pending;
    private bool closed;
    private bool cancellationSettled;

    internal ManagementEntraMetadata(IHostApplicationLifetime lifetime, HttpMessageHandler? handler = null)
    {
        client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.MaxResponseContentBufferSize = 1024 * 1024;
        disposal = new(DisposeCoreAsync);
        stopRegistration = lifetime.ApplicationStopping.Register(Close);
    }

    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (address.Length > 4096 || !Uri.TryCreate(address, UriKind.Absolute, out Uri? target)
            || target.Scheme != Uri.UriSchemeHttps || target.UserInfo.Length != 0 || target.Fragment.Length != 0)
        {
            throw new ArgumentException("Management identity metadata requires a bounded absolute HTTPS address.", nameof(address));
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (pending == 4)
            {
                throw new InvalidOperationException("The Management identity metadata request bound was reached.");
            }

            pending++;
        }

        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancel, stopping.Token);
            request.CancelAfter(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await client.GetAsync(target,
                HttpCompletionOption.ResponseContentRead, request.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(request.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                pending--;
                if (cancellationSettled && pending == 0)
                {
                    drained.TrySetResult();
                }
            }
        }
    }

    private void Close()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
        }

        try
        {
            stopping.Cancel();
        }
        finally
        {
            lock (gate)
            {
                cancellationSettled = true;
                if (pending == 0)
                {
                    drained.TrySetResult();
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return new(disposal.Value);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            Close();
        }
        finally
        {
            await drained.Task.ConfigureAwait(false);
            stopRegistration.Dispose();
            try
            {
                client.Dispose();
            }
            finally
            {
                stopping.Dispose();
            }
        }
    }
}
