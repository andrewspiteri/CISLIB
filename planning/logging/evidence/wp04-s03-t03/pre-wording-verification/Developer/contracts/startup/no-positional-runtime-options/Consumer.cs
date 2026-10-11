using CIS; using CIS.Logging; class Consumer { void Check(EnvironmentSnapshot environment) { CoreLogRuntime.Create(environment, 100); } }
