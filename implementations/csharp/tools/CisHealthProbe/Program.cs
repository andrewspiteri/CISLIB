using CIS.Healthcheck.Pipes;

return await HealthProbeClient.RunAsync(args, Console.OpenStandardOutput(), Console.Error).ConfigureAwait(false);
