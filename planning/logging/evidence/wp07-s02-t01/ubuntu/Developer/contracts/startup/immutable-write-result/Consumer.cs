using CIS.Logging; class Consumer { void Check(LogWriteResult result) { result.Outcome = LogWriteOutcome.Accepted; } }
