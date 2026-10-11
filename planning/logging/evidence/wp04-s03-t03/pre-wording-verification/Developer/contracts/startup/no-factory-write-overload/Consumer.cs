using CIS.Logging; class Consumer { void Check(CoreLogger logger) { logger.Write(level: CoreLogLevel.Information, factory: () => new LogEventData()); } }
