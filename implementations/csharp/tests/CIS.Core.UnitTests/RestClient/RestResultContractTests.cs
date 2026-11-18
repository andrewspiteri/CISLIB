using System.Text.Json;
using System.Text;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestResultContractTests
{
    [TestMethod]
    [DataRow("present-zero")]
    [DataRow("present-false")]
    [DataRow("present-null")]
    [DataRow("present-empty-string")]
    [DataRow("absent-int")]
    [DataRow("absent-string")]
    [DataRow("http-failure")]
    [DataRow("business-failure")]
    public void PortableLiteralResultVectorsPreserveTypedDefaultPresenceAndFailure(string id)
    {
        using JsonDocument document = Fixture();
        JsonElement row = document.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        switch (row.GetProperty("type").GetString())
        {
            case "int": Check(row, row.GetProperty("value").GetInt32()); break;
            case "bool": Check(row, row.GetProperty("value").GetBoolean()); break;
            case "string": Check<string?>(row, row.GetProperty("value").GetString()); break;
            default: Assert.Fail("Unknown portable result type."); break;
        }
    }

    [TestMethod]
    public void ContextAndErrorPrefixDetachCallerCollectionsAndKeepContentHeaderValues()
    {
        string[] supplied = ["one", "two"];
        var headers = new Dictionary<string, IReadOnlyList<string>> { ["Content-Type"] = supplied };
        byte[] bytes = [1, 2, 3];
        var context = new RestResultContext(headers: headers, errorBody: new(bytes, "application/problem+json"),
            failureKind: RestFailureKind.Http, httpStatusCode: 400, errorBodyTruncated: true, errorMessageTruncated: false);
        supplied[0] = "changed"; headers.Clear(); bytes[0] = 9;
        CollectionAssert.AreEqual(new[] { "one", "two" }, context.Headers["content-type"].ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, context.ErrorBody!.Bytes.ToArray());
        Assert.IsTrue(context.ErrorBodyTruncated); Assert.IsFalse(context.ErrorMessageTruncated);
    }

    [TestMethod]
    public void GuardedFactoriesRejectContradictoryPresenceAndFailureWithoutInvokingValueEquality()
    {
        var absent = new RestResultContext();
        var present = new RestResultContext(hasValue: true);
        var failed = new RestResultContext(failureKind: RestFailureKind.Http, httpStatusCode: 400);
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.FromValue(0, absent));
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.Empty(present));
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.Failed("http.400", "Rejected.", absent));
        Assert.ThrowsExactly<ArgumentException>(() => new RestResultContext(hasValue: true, failureKind: RestFailureKind.Business));
        var value = new EqualityIsNotAResultOperation();
        Assert.AreSame(value, RestResult<EqualityIsNotAResultOperation>.FromValue(value, present).Value);
        Assert.IsNull(RestResult<EqualityIsNotAResultOperation>.Failed("http.400", "Rejected.", failed).Value);
    }

    [TestMethod]
    [DataRow(199)]
    [DataRow(300)]
    [DataRow(503)]
    public void SuccessCannotHideAnActualNonSuccessHttpStatus(int status)
    {
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.FromValue(7, new(httpStatusCode: status, hasValue: true)));
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.Empty(new(httpStatusCode: status)));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(200)]
    [DataRow(299)]
    public void SuccessAcceptsTheSuccessfulStatusBoundariesAndDetachedUnknownStatus(int supplied)
    {
        int? status = supplied == 0 ? null : supplied;
        Assert.IsTrue(RestResult<int>.FromValue(7, new(httpStatusCode: status, hasValue: true)).Success);
        Assert.IsTrue(RestResult<int>.Empty(new(httpStatusCode: status)).Success);
    }

    [TestMethod]
    public void HttpFailureRequiresItsActualStatusWhileSuccessfulHttpCanFailLaterProcessing()
    {
        foreach (int? status in new int?[] { null, 200, 299 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.Failed("http.503", "Unavailable.",
                new(httpStatusCode: status, failureKind: RestFailureKind.Http)));
        }
        Assert.IsFalse(RestResult<int>.Failed("http.503", "Unavailable.", new(httpStatusCode: 503, failureKind: RestFailureKind.Http)).Success);
        foreach (RestFailureKind kind in new[] { RestFailureKind.Business, RestFailureKind.InvalidResponse, RestFailureKind.LimitExceeded, RestFailureKind.Timeout })
        {
            var result = RestResult<int>.Failed("client.processing_failed", "Processing failed.", new(httpStatusCode: 200, failureKind: kind));
            Assert.IsFalse(result.Success); Assert.IsFalse(result.Context.HasValue); Assert.AreEqual(0, result.Value);
        }
    }

    [TestMethod]
    public void DeclaredFailuresClipUnicodeScalarsAndKeepMessageAndBodyTruncationIndependent()
    {
        var failure = new RestDeclaredFailure(RestFailureKind.Preparation, "client.preparation_failed", string.Concat(Enumerable.Repeat("\U0001f642", 1025)));
        (string message, bool truncated) = failure.BoundedMessage(1024);
        Assert.AreEqual(2048, message.Length); Assert.IsTrue(truncated);
        Assert.AreEqual(1024, message.EnumerateRunes().Count());
        Assert.AreEqual(("Safe.", false), new RestDeclaredFailure(RestFailureKind.Authentication, "client.authentication_failed", "Safe.").BoundedMessage(1024));
    }

    private static void Check<T>(JsonElement row, T expected)
    {
        bool success = row.GetProperty("success").GetBoolean(), hasValue = row.GetProperty("hasValue").GetBoolean();
        RestFailureKind kind = Enum.Parse<RestFailureKind>(row.GetProperty("failureKind").GetString()!);
        var context = new RestResultContext(httpStatusCode: row.TryGetProperty("status", out JsonElement status) ? status.GetInt32() : null,
            hasValue: hasValue, failureKind: kind, clientName: "orders", operationName: "request", attemptCount: 1);
        RestResult<T> actual = success
            ? hasValue ? RestResult<T>.FromValue(expected, context) : RestResult<T>.Empty(context)
            : RestResult<T>.Failed(row.GetProperty("errorCode").GetString()!, row.GetProperty("errorMessage").GetString()!, context);
        Assert.AreEqual(success, actual.Success); Assert.AreEqual(hasValue, actual.Context.HasValue);
        Assert.AreEqual(expected, actual.Value); Assert.AreEqual(kind, actual.Context.FailureKind);
        Assert.AreEqual(success ? null : row.GetProperty("errorCode").GetString(), actual.ErrorCode);
        Assert.AreEqual(success ? null : row.GetProperty("errorMessage").GetString(), actual.ErrorMessage);
    }
    private static JsonDocument Fixture()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "conformance", "rest-client", "v1", "result-cases.json");
            if (File.Exists(path)) { return JsonDocument.Parse(File.ReadAllBytes(path)); }
        }
        throw new AssertFailedException("The portable result fixture was not found.");
    }
    private sealed class EqualityIsNotAResultOperation
    {
        public override bool Equals(object? obj) => throw new InvalidOperationException("Do not classify result presence using DTO equality.");
        public override int GetHashCode() => throw new InvalidOperationException();
    }
}
