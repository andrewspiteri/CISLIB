using CIS.Logging; using CIS.Logging.MicrosoftLogger; class Consumer { void Check(CoreLogRuntime runtime) { new CoreLoggerProvider(runtime: runtime, ownsRuntime: true); } }
