using CIS.Security;
using CIS.Transactions;

namespace CIS.Operations;

/// <summary>A bounded submitted identity and application input. RequestedScope remains untrusted until resolved.</summary>
public sealed class OperationRequest<TRequest>
{
    /// <summary>Creates a request without granting scope authority or copying arbitrary application objects.</summary>
    public OperationRequest(string operationName, string requestedScope, string key, TRequest input)
    {
        OperationName = OperationText.Alias(operationName, nameof(operationName));
        RequestedScope = OperationText.Utf8(requestedScope, 256, nameof(requestedScope));
        Key = OperationText.Key(key, nameof(key));
        Input = input;
    }

    /// <summary>Gets the registered alias.</summary>
    public string OperationName
    {
        get;
    }
    /// <summary>Gets the requested logical scope, subject to the trusted application resolver and authorizer.</summary>
    public string RequestedScope
    {
        get;
    }
    /// <summary>Gets the unchanged caller idempotency key.</summary>
    public string Key
    {
        get;
    }
    /// <summary>Gets input to the registered trusted normalizer; handlers receive its owned snapshot.</summary>
    public TRequest Input
    {
        get;
    }
}

/// <summary>A bounded lookup request, with no caller-owned write transaction.</summary>
public sealed class OperationLookupRequest
{
    /// <summary>Creates a lookup request without treating possession of a key or scope text as authority.</summary>
    public OperationLookupRequest(string operationName, string requestedScope, string key)
    {
        OperationName = OperationText.Alias(operationName, nameof(operationName));
        RequestedScope = OperationText.Utf8(requestedScope, 256, nameof(requestedScope));
        Key = OperationText.Key(key, nameof(key));
    }

    /// <summary>Gets the registered alias.</summary>
    public string OperationName
    {
        get;
    }
    /// <summary>Gets scope text that the application must resolve authoritatively.</summary>
    public string RequestedScope
    {
        get;
    }
    /// <summary>Gets the original opaque key.</summary>
    public string Key
    {
        get;
    }
}

/// <summary>Executes one short durable attempt or independently retrieves its original committed result.</summary>
public interface IDurableOperationService
{
    /// <summary>Claims or compares identity and stages a completed result in the supplied transaction.</summary>
    /// <remarks>Await the entire method before completing or disposing the owner. Prepared is not committed.
    /// The handler runs outside native callbacks and receives the original context and an owned normalized input.</remarks>
    Task<OperationExecution<TResult>> ExecuteAsync<TRequest, TResult>(ILocalTransactionContext context,
        OperationRequest<TRequest> request,
        Func<ILocalTransactionContext, TRequest, CancellationToken, Task<OperationDecision<TResult>>> handler,
        CancellationToken cancellationToken = default);

    /// <summary>Reads through a fresh authoritative scope; it never invokes a business handler or completes a caller's owner.</summary>
    Task<OperationLookup<TResult>> GetResultAsync<TResult>(OperationLookupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A trusted application-resolved logical scope and actual protected resource.</summary>
public sealed class OperationScope
{
    /// <summary>Copies the logical scope and exact resource; it does not itself establish an authenticated caller.</summary>
    public OperationScope(string scope, string resourceType, string resourceIdentifier)
    {
        Scope = OperationText.Utf8(scope, 256, nameof(scope));
        ResourceType = SecurityText.Identifier(resourceType, nameof(resourceType));
        ResourceIdentifier = SecurityText.Identifier(resourceIdentifier, nameof(resourceIdentifier));
    }

    /// <summary>Gets the trusted logical uniqueness scope.</summary>
    public string Scope
    {
        get;
    }
    /// <summary>Gets the actual resource type.</summary>
    public string ResourceType
    {
        get;
    }
    /// <summary>Gets the actual resource identifier.</summary>
    public string ResourceIdentifier
    {
        get;
    }
}

/// <summary>Resolves requested scope through application policy; copied caller data is never authentication.</summary>
public interface IOperationScopeResolver
{
    /// <summary>Resolves the exact application scope/resource before native work or result disclosure.</summary>
    ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
        CancellationToken cancellationToken = default);
}
