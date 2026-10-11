using CIS.Configuration;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class UnicodeScalarTextTests
{
    [TestMethod]
    [DataRow("empty", true, false, false)]
    [DataRow("space", true, true, false)]
    [DataRow("255-emoji", true, true, true)]
    [DataRow("256-emoji", true, false, false)]
    [DataRow("control-c0", true, false, false)]
    [DataRow("control-c1", true, false, false)]
    [DataRow("format-zwj", true, true, true)]
    [DataRow("high", false, false, false)]
    [DataRow("low", false, false, false)]
    [DataRow("interrupted", false, false, false)]
    [DataRow("composed", true, true, true)]
    [DataRow("decomposed", true, true, true)]
    [DataRow("pair", true, true, true)]
    public void PublicStringsKeysAndExistingIdentitiesShareScalarRulesWithDistinctPolicies(string scenario, bool validString, bool validKey, bool validIdentity)
    {
        string text = scenario switch
        {
            "empty" => "",
            "space" => " ",
            "255-emoji" => string.Concat(Enumerable.Repeat("\U0001f600", 255)),
            "256-emoji" => string.Concat(Enumerable.Repeat("\U0001f600", 256)),
            "control-c0" => "x\0",
            "control-c1" => "x\u009f",
            "format-zwj" => "x\u200d",
            "high" => new string((char)0xd800, 1),
            "low" => new string((char)0xdc00, 1),
            "interrupted" => new string([(char)0xd800, 'x', (char)0xdc00]),
            "composed" => "\u00e9",
            "decomposed" => "e\u0301",
            _ => "\U0001f600"
        };
        if (validString)
        {
            Assert.AreEqual(text, LogValue.String(text).Scalar.Text);
        }
        else
        {
            Assert.ThrowsExactly<ArgumentException>(() => LogValue.String(text));
        }
        var source = new Dictionary<string, LogValue> { [text] = LogValue.Null() };
        if (validKey)
        {
            Assert.IsTrue(LogValue.Map(source).MapProperties.ContainsKey(text));
        }
        else
        {
            Assert.ThrowsExactly<ArgumentException>(() => LogValue.Map(source));
        }
        Assert.AreEqual(validIdentity, ConfigurationIdentityValidator.IsValid(text, 255));
    }

    [TestMethod]
    public void EveryC0AndC1ControlIsPreservedInValuesAndRejectedInKeysAndIdentities()
    {
        foreach (int scalar in Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)))
        {
            string text = "safe" + (char)scalar;
            Assert.AreEqual(text, LogValue.String(text).Scalar.Text);
            Assert.ThrowsExactly<ArgumentException>(() => LogValue.Map(new Dictionary<string, LogValue> { [text] = LogValue.Null() }));
            Assert.IsFalse(ConfigurationIdentityValidator.IsValid(text, 255));
        }
    }

    [TestMethod]
    public void MalformedUnicodeAfterALargeValidStringPrefixIsStillInvalid()
    {
        string text = new string('x', 1_048_576) + (char)0xd800;
        ArgumentException failure = Assert.ThrowsExactly<ArgumentException>(() => LogValue.String(text));
        Assert.AreEqual("value", failure.ParamName);
        Assert.IsFalse(failure.Message.Contains(text, StringComparison.Ordinal));
    }
}
