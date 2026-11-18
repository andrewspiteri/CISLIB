using System.Text;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestRequestFinalizerTests
{
    [TestMethod]
    [DataRow("foreign-origin")]
    [DataRow("trace")]
    [DataRow("host")]
    [DataRow("replacement-body")]
    public async Task ContentFactoryCannotBypassFinalRequestGuardsThroughARetainedContext(string mutation)
    {
        RestRequestContext? retained = null;
        int authenticationCalls = 0, nativePreparations = 0, opens = 0;
        MemoryStream? input = null;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "https://example.test/api/",
            ConfigureRequest = (request, _) => { retained = request; return Task.CompletedTask; },
            Authenticate = (_, _) => { authenticationCalls++; return Task.CompletedTask; }
        };
        RestBody body = RestHttpBody.Reopen(_ =>
        {
            opens++;
            switch (mutation)
            {
                case "foreign-origin": retained!.Url = "https://foreign.test/orders"; break;
                case "trace": retained!.Method = "TRACE"; break;
                case "host": retained!.Headers.Set("Host", "foreign.test"); break;
                case "replacement-body": retained!.Body = RestBody.Text("replacement"); break;
            }
            input = new MemoryStream([1, 2]);
            return ValueTask.FromResult<Stream>(input);
        });
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, new() { Body = body });
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "POST", "orders", 0);
        RestHttpContentLease? lease = null;
        try
        {
            RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
                async token => { lease = await RestHttpContentLease.AcquireAsync(request.Body, null, null, token); },
                NoSigning, default, () => nativePreparations++);
            AssertPreparation(failure, "client.preparation_failed");
            Assert.AreEqual(1, opens); Assert.AreEqual(0, authenticationCalls); Assert.AreEqual(0, nativePreparations);
            AssertSealed(request);
        }
        finally { if (lease is not null) { await lease.DisposeAsync(); } }
        Assert.IsNotNull(input); Assert.IsFalse(input.CanRead, "The acquired factory stream remains owned even after finalization rejects the mutation.");
    }

    [TestMethod]
    [DataRow("CONNECT")]
    [DataRow("upgrade-header")]
    [DataRow("upgrade-connection")]
    public async Task UnsupportedCapabilitiesHaveTheirPortableCodeBeforeContentOrAuthentication(string capability)
    {
        int contentPreparations = 0, authenticationCalls = 0;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "https://example.test/",
            Authenticate = (_, _) => { authenticationCalls++; return Task.CompletedTask; }
        };
        var input = new RestRequestOptions();
        if (capability == "upgrade-header") { input.Headers.Set("Upgrade", "websocket"); }
        if (capability == "upgrade-connection") { input.Headers.Set("Connection", "keep-alive, UpGrAdE"); }
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, input);
        RestRequestContext request = RestRequestFinalizer.Create(client, call, capability == "CONNECT" ? "CONNECT" : "GET", "orders", 0);
        RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
            _ => { contentPreparations++; return ValueTask.CompletedTask; }, NoSigning, default);
        AssertPreparation(failure, "client.unsupported_operation");
        Assert.AreEqual(0, contentPreparations); Assert.AreEqual(0, authenticationCalls); AssertSealed(request);
    }

    [TestMethod]
    [DataRow("Authorization", true)]
    [DataRow("Proxy-Authorization", true)]
    [DataRow("Cookie", true)]
    [DataRow("X-Api-Key", false)]
    public async Task InheritedCredentialHeadersNeedOriginsEvenWhenIntrinsicNamesWereRemovedFromTheDeclaredList(string name, bool clearDeclared)
    {
        var options = new RestClientOptions("orders");
        if (clearDeclared) { options.CredentialHeaders.Clear(); }
        else { options.CredentialHeaders.Add(name); }
        options.Headers.Set(name, "inherited-secret");
        Assert.ThrowsExactly<ArgumentException>(() => RestClientSnapshot.Capture(options));
        options.AllowedOrigins.Add("https://example.test");
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, null);
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "GET", "https://foreign.test/orders", 0);
        int contentPreparations = 0;
        RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
            _ => { contentPreparations++; return ValueTask.CompletedTask; }, NoSigning, default);
        AssertPreparation(failure, "client.preparation_failed");
        Assert.AreEqual(0, contentPreparations); AssertSealed(request);
    }

    [TestMethod]
    public async Task AuthenticationSeesExactQueryAndCanonicalNativeHeadersAndFinalSendUsesItsCredentialQuery()
    {
        const string ordinaryUrl = "https://example.test/api/?keep=%2f&flag&repeat=old&q=a%20b&q=a%2520b&repeat=%2B";
        const string signedUrl = ordinaryUrl + "&signature=a%2Bb%20%2F";
        RestAuthenticationContext? retainedAuthentication = null;
        HttpRequestMessage? native = null;
        RestHttpContentLease? lease = null;
        ReadOnlyMemory<byte> signed = default;
        int authenticationCalls = 0;
        var options = new RestClientOptions("orders") { BaseUrl = "https://example.test/api/?keep=%2f&flag&repeat=old&q=old" };
        options.Headers.Set("Content-Type", "text/plain;charset=utf-8");
        options.CredentialQueryParameters.Add("signature");
        options.Authenticate = async (authentication, token) =>
        {
            authenticationCalls++; retainedAuthentication = authentication;
            Assert.AreEqual(ordinaryUrl, authentication.Url);
            Assert.AreEqual("text/plain; charset=utf-8", authentication.Headers.Snapshot()["Content-Type"].Single());
            authentication.Query.Set("signature", "a+b /");
            Assert.AreEqual(signedUrl, authentication.Url);
            authentication.Headers.Set("Authorization", "Bearer    secret");
            Assert.AreEqual("Bearer secret", authentication.Headers.Snapshot()["Authorization"].Single());
            Assert.AreEqual("Bearer secret", native!.Headers.Authorization!.ToString());
            Assert.ThrowsExactly<InvalidOperationException>(() => authentication.Headers.Set("Content-Type", "application/json"));
            Assert.ThrowsExactly<InvalidOperationException>(() => authentication.Query.Set("q", "changed"));
            signed = await authentication.GetBodyBytesAsync(32, token);
        };
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        var input = new RestRequestOptions { Body = RestBody.Text("wire value") };
        input.Query.Set("q", "a b"); input.Query.Append("q", "a%20b"); input.Query.Append("repeat", "+");
        RestCallSnapshot call = RestCallSnapshot.Capture(client, input);
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "POST", "", 0);
        try
        {
            RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
                async token => { lease = await RestHttpContentLease.AcquireAsync(request.Body, null, null, token); },
                (maximum, token) => lease!.GetSigningBytesAsync(maximum, token), default,
                () => native = RestNativeRequest.Create(request, lease!.Content));
            Assert.IsNull(failure); Assert.AreEqual(1, authenticationCalls); Assert.AreEqual(signedUrl, request.Url);
            AssertSealed(request);
            RestNativeRequest.Finalize(native!, request);
            Assert.AreEqual(signedUrl, native!.RequestUri!.AbsoluteUri);
            Assert.AreEqual("text/plain; charset=utf-8", native.Content!.Headers.ContentType!.ToString());
            using var wire = new MemoryStream(); await native.Content.CopyToAsync(wire);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("wire value"), signed.ToArray());
            CollectionAssert.AreEqual(signed.ToArray(), wire.ToArray());
            Assert.IsNotNull(retainedAuthentication);
            Assert.ThrowsExactly<InvalidOperationException>(() => retainedAuthentication.Headers.Remove("Authorization"));
            Assert.ThrowsExactly<InvalidOperationException>(() => retainedAuthentication.Query.Append("signature", "later"));
        }
        finally { native?.Dispose(); if (lease is not null) { await lease.DisposeAsync(); } }
    }

    [TestMethod]
    public async Task DeclaredContentCredentialUsesNativeContentHeadersAndFailedReplacementPreservesItsPriorValue()
    {
        HttpRequestMessage? native = null;
        RestHttpContentLease? lease = null;
        var options = new RestClientOptions("orders") { BaseUrl = "https://example.test/" };
        options.CredentialHeaders.Add("Content-MD5");
        options.Authenticate = (authentication, _) =>
        {
            authentication.Headers.Set("Content-MD5", "AQID");
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, native!.Content!.Headers.ContentMD5!);
            Assert.ThrowsExactly<FormatException>(() => authentication.Headers.Set("Content-MD5", "%%%"));
            Assert.AreEqual("AQID", authentication.Headers.Snapshot()["Content-MD5"].Single());
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, native.Content.Headers.ContentMD5!);
            Assert.IsTrue(authentication.Headers.Remove("Content-MD5"));
            Assert.IsNull(native.Content.Headers.ContentMD5);
            authentication.Headers.Set("Content-MD5", "BAU=");
            return Task.CompletedTask;
        };
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, new() { Body = RestBody.Text("value") });
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "POST", "orders", 0);
        try
        {
            RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
                async token => { lease = await RestHttpContentLease.AcquireAsync(request.Body, null, null, token); }, NoSigning, default,
                () => native = RestNativeRequest.Create(request, lease!.Content));
            Assert.IsNull(failure); RestNativeRequest.Finalize(native!, request);
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, native!.Content!.Headers.ContentMD5!);
            Assert.IsFalse(native.Headers.TryGetValues("Content-MD5", out _)); AssertSealed(request);
        }
        finally { native?.Dispose(); if (lease is not null) { await lease.DisposeAsync(); } }
    }

    [TestMethod]
    public async Task TraceOmitsInheritedCredentialHeadersAndBaseQueryWithoutRunningInheritedAuthentication()
    {
        int authenticationCalls = 0;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "https://example.test/api/?token=inherited&keep=one",
            Authenticate = (_, _) => { authenticationCalls++; return Task.CompletedTask; }
        };
        options.CredentialHeaders.Add("X-Api-Key"); options.CredentialQueryParameters.Add("token");
        options.Headers.Set("Authorization", "Bearer inherited"); options.Headers.Set("Cookie", "sid=inherited"); options.Headers.Set("X-Api-Key", "inherited");
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, null);
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "TRACE", "", 0);
        HttpRequestMessage? native = null;
        try
        {
            RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
                _ => ValueTask.CompletedTask, NoSigning, default, () => native = RestNativeRequest.Create(request, null));
            Assert.IsNull(failure); Assert.AreEqual(0, authenticationCalls);
            RestNativeRequest.Finalize(native!, request);
            Assert.AreEqual("https://example.test/api/?keep=one", native!.RequestUri!.AbsoluteUri);
            Assert.IsNull(native.Content);
            Assert.IsFalse(native.Headers.Contains("Authorization")); Assert.IsFalse(native.Headers.Contains("Cookie")); Assert.IsFalse(native.Headers.Contains("X-Api-Key"));
            AssertSealed(request);
        }
        finally { native?.Dispose(); }
    }

    [TestMethod]
    [DataRow("body")]
    [DataRow("authentication")]
    [DataRow("header")]
    [DataRow("query")]
    [DataRow("customized-header")]
    [DataRow("customized-query")]
    [DataRow("url-query")]
    public async Task TraceRejectsExplicitAndCustomizedCredentialInputsBeforeContentOrAuthentication(string source)
    {
        int contentPreparations = 0, authenticationCalls = 0;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "https://example.test/",
            Authenticate = (_, _) => { authenticationCalls++; return Task.CompletedTask; }
        };
        options.CredentialQueryParameters.Add("token");
        var input = new RestRequestOptions();
        switch (source)
        {
            case "body": input.Body = RestBody.Text("forbidden"); break;
            case "authentication": input.Authenticate = (_, _) => { authenticationCalls++; return Task.CompletedTask; }; break;
            case "header": input.Headers.Set("Authorization", "Bearer explicit"); break;
            case "query": input.Query.Set("token", "explicit"); break;
            case "customized-header": input.ConfigureRequest = (request, _) => { request.Headers.Set("Cookie", "sid=customized"); return Task.CompletedTask; }; break;
            case "customized-query": input.ConfigureRequest = (request, _) => { request.Query.Set("token", "customized"); return Task.CompletedTask; }; break;
        }
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, input);
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "TRACE", source == "url-query" ? "?token=explicit" : "orders", 0);
        RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(request, client, call,
            _ => { contentPreparations++; return ValueTask.CompletedTask; }, NoSigning, default);
        AssertPreparation(failure, "client.preparation_failed");
        Assert.AreEqual(0, contentPreparations); Assert.AreEqual(0, authenticationCalls); AssertSealed(request);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuthenticationCannotDetachStartedSigningWorkOrRestartItAfterFinalization(bool faulted)
    {
        var defect = new InvalidOperationException("Controlled signing defect.");
        var codec = new HeldSigningCodec(faulted ? defect : null);
        RestAuthenticationContext? retained = null;
        Task<ReadOnlyMemory<byte>>? signing = null;
        RestHttpContentLease? lease = null;
        HttpRequestMessage? native = null;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "https://example.test/",
            Authenticate = (authentication, token) =>
            {
                retained = authentication;
                signing = authentication.GetBodyBytesAsync(32, token).AsTask();
                return Task.CompletedTask; // The authenticator deliberately does not await its owned work.
            }
        };
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, new() { Body = RestBody.Text("value") });
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "POST", "orders", 0);
        Task<RestDeclaredFailure?> finalization = RestRequestFinalizer.PrepareAsync(request, client, call,
            async token => { lease = await RestHttpContentLease.AcquireAsync(request.Body, codec, null, token); },
            (maximum, token) => lease!.GetSigningBytesAsync(maximum, token), default,
            () => native = RestNativeRequest.Create(request, lease!.Content)).AsTask();
        try
        {
            await codec.Entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsNotNull(signing); Assert.IsFalse(signing.IsCompleted);
            Assert.IsFalse(finalization.IsCompleted, "Finalization must join started materialization even when authentication returned early.");
            codec.Release.TrySetResult();
            if (faulted)
            {
                InvalidOperationException actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => finalization.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreSame(defect, actual);
            }
            else
            {
                Assert.IsNull(await finalization.WaitAsync(RestHttpTestSupport.Deadline));
                ReadOnlyMemory<byte> signed = await signing.WaitAsync(RestHttpTestSupport.Deadline);
                RestNativeRequest.Finalize(native!, request);
                using var wire = new MemoryStream(); await native!.Content!.CopyToAsync(wire);
                CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("signed value"), signed.ToArray());
                CollectionAssert.AreEqual(signed.ToArray(), wire.ToArray());
            }
            AssertSealed(request); Assert.IsNotNull(retained);
            InvalidOperationException closed = await Assert.ThrowsAsync<InvalidOperationException>(() => retained.GetBodyBytesAsync(32).AsTask());
            Assert.AreNotSame(defect, closed, "Late access must reject the closed authentication context rather than replay a materialization fault.");
            Assert.AreEqual(1, codec.Calls, "A retained authenticator cannot start more body work after completion.");
        }
        finally
        {
            codec.Release.TrySetResult();
            try
            {
                try { await finalization.WaitAsync(RestHttpTestSupport.Deadline); }
                catch (InvalidOperationException error) when (ReferenceEquals(error, defect)) { }
            }
            finally
            {
                try
                {
                    if (signing is not null)
                    {
                        try { await signing.WaitAsync(RestHttpTestSupport.Deadline); }
                        catch (InvalidOperationException error) when (ReferenceEquals(error, defect)) { }
                    }
                }
                finally { native?.Dispose(); if (lease is not null) { await lease.DisposeAsync(); } }
            }
        }
    }

    private sealed class HeldSigningCodec(Exception? failure) : IRestContentCodec
    {
        internal TaskCompletionSource Entered { get; } = RestHttpTestSupport.Signal();
        internal TaskCompletionSource Release { get; } = RestHttpTestSupport.Signal();
        internal int Calls { get; private set; }
        public bool CanDecode(Type resultType, RestResponseFormat format) => false;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        {
            Calls++; Entered.TrySetResult(); await Release.Task.WaitAsync(token);
            if (failure is not null) { throw failure; }
            await destination.WriteAsync(Encoding.UTF8.GetBytes("signed value"), token);
        }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
            => throw new NotSupportedException();
    }

    private static ValueTask<ReadOnlyMemory<byte>> NoSigning(int maximum, CancellationToken token)
        => throw new AssertFailedException("Rejected or unsigned requests cannot request a signing representation.");
    private static void AssertPreparation(RestDeclaredFailure? failure, string code)
    { Assert.IsNotNull(failure); Assert.AreEqual(RestFailureKind.Preparation, failure.Kind); Assert.AreEqual(code, failure.Code); }
    private static void AssertSealed(RestRequestContext request)
    {
        Assert.IsTrue(request.IsSealed);
        Assert.ThrowsExactly<InvalidOperationException>(() => request.Method = "DELETE");
        Assert.ThrowsExactly<InvalidOperationException>(() => request.Url = "https://foreign.test/");
        Assert.ThrowsExactly<InvalidOperationException>(() => request.Headers.Set("Authorization", "later"));
        Assert.ThrowsExactly<InvalidOperationException>(() => request.Query.Set("token", "later"));
    }
}
