using CIS.Logging; class Consumer { void Check(LogEventData data) { data.Severity = CoreLogLevel.Error; } }
