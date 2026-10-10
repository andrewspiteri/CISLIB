# Microsoft logger integration

Implement the adapter from Microsoft `ILogger`/`ILoggerProvider` and host scopes to the Core logging module here, using namespace `CIS.Logging.MicrosoftLogger`.

It is compiled into the same Core assembly. The initial Microsoft logging abstractions dependency is approved; provider implementation, filters, scope mapping and runtime ownership are pending logging functionality.

Third-party integrations such as NLog belong in separately built and packaged projects.
