using System.Security.Cryptography;
using System.Text;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class TypeManagerVerificationInputTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PolicyHashDescribesTheOwnedBytesIncludingOptionalBom(bool bom)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "policy.json");
        byte[] json = Encoding.UTF8.GetBytes("{\"runtime\":\"10.0.12\"}");
        byte[] bytes = bom ? [.. Encoding.UTF8.Preamble, .. json] : json;
        File.WriteAllBytes(path, bytes);
        string expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var parsed = TypeManagerVerificationInputs.Read(path, out string hash);
        Assert.AreEqual(expected, hash);
        Assert.AreEqual("10.0.12", parsed["runtime"]!.GetValue<string>());
        Assert.AreEqual("10.0.12", TypeManagerVerificationInputs.ReadPinned(path, expected, "pin differs")["runtime"]!.GetValue<string>());
        File.WriteAllText(path, "{\"runtime\":\"different\"}");
        Assert.AreEqual("10.0.12", parsed["runtime"]!.GetValue<string>());
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() =>
            TypeManagerVerificationInputs.ReadPinned(path, expected, "pin differs")).Message, "pin differs");
    }
}
