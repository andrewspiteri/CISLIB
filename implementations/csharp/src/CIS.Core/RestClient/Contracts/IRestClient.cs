namespace CIS.RestClient;

/// <summary>Runs asynchronous typed HTTP operations through one owned execution path.</summary>
public interface IRestClient
{
    /// <summary>Runs GET.</summary>
    Task<RestResult<T>> Get<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs POST.</summary>
    Task<RestResult<T>> Post<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs PUT.</summary>
    Task<RestResult<T>> Put<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs PATCH.</summary>
    Task<RestResult<T>> Patch<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs DELETE.</summary>
    Task<RestResult<T>> Delete<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs OPTIONS.</summary>
    Task<RestResult<T>> Options<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs TRACE without a body or credentials.</summary>
    Task<RestResult<T>> Trace<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs HEAD without decoding a response value.</summary>
    Task<RestResult<RestNoValue>> Head(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Runs one valid method token; unsupported channel capabilities fail before dispatch.</summary>
    Task<RestResult<T>> Send<T>(string method, string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default);
}
