namespace CIS.RestClient.Examples.RestApi;

// A self-test-only origin with real HTTP. Normal mode uses the explicit backend URL.
internal static class SelfTestOrigin
{
    internal static async Task<WebApplication> StartAsync(CancellationToken token)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build(); int retries = 0;
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization != "Bearer example-token")
            { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            await next(context);
        });
        app.MapPost("/orders", async Task<IResult> (OrderCommand command, HttpContext context) =>
        {
            if (command.Quantity <= 0) { return Results.BadRequest(); }
            await context.Response.WriteAsJsonAsync(new OrderReceipt(command.Item, true), context.RequestAborted);
            return Results.Empty;
        });
        app.MapGet("/orders/retry", IResult () => Interlocked.Increment(ref retries) <= 2
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(new OrderReceipt("retried", true)));
        app.MapGet("/orders/declined", () => new OrderReceipt("declined", false, "Stock unavailable."));
        app.MapGet("/orders/empty", () => Results.NoContent());
        try { await app.StartAsync(token); return app; }
        catch
        {
            try { await app.StopAsync(CancellationToken.None); }
            finally { await app.DisposeAsync(); }
            throw;
        }
    }
}
