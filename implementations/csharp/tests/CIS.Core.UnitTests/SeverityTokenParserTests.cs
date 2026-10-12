using System.Globalization;
using CIS.Logging;
using CIS.Logging.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class SeverityTokenParserTests
{
    [TestMethod]
    [DataRow("Trace", 0)]
    [DataRow("Debug", 1)]
    [DataRow("Information", 2)]
    [DataRow("Warning", 3)]
    [DataRow("Error", 4)]
    [DataRow("Critical", 5)]
    [DataRow("None", 6)]
    [DataRow("trace", 0)]
    [DataRow("debug", 1)]
    [DataRow("information", 2)]
    [DataRow("warning", 3)]
    [DataRow("error", 4)]
    [DataRow("critical", 5)]
    [DataRow("none", 6)]
    [DataRow(" \ttRaCe\r\n", 0)]
    [DataRow(" \tdEbUg\r\n", 1)]
    [DataRow(" \tiNfOrMaTiOn\r\n", 2)]
    [DataRow(" \twArNiNg\r\n", 3)]
    [DataRow(" \teRrOr\r\n", 4)]
    [DataRow(" \tcRiTiCaL\r\n", 5)]
    [DataRow("\u2003nOnE\u2003", 6)]
    [DataRow("TRC", 0)]
    [DataRow(" \ttrc\r\n", 0)]
    [DataRow("DBG", 1)]
    [DataRow(" \tdbg\r\n", 1)]
    [DataRow("INF", 2)]
    [DataRow(" \tinf\r\n", 2)]
    [DataRow("INFO", 2)]
    [DataRow(" \tinfo\r\n", 2)]
    [DataRow("WRN", 3)]
    [DataRow(" \twrn\r\n", 3)]
    [DataRow("WARN", 3)]
    [DataRow(" \twarn\r\n", 3)]
    [DataRow("ERR", 4)]
    [DataRow(" \terr\r\n", 4)]
    [DataRow("CRT", 5)]
    [DataRow(" \tcrt\r\n", 5)]
    [DataRow("CRIT", 5)]
    [DataRow(" \tcrit\r\n", 5)]
    public void NamedTokensAndAliasesHaveDefinedValues(string token, int expected)
    {
        ConfigurationTokenState state = SeverityTokenParser.Parse(token, out CoreLogLevel? severity);

        Assert.AreEqual(ConfigurationTokenState.Valid, state);
        Assert.IsNotNull(severity);
        Assert.AreEqual(expected, (int)severity.Value);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t\r\n")]
    [DataRow("\u2003\u00a0")]
    public void BlankValuesAreAbsentAndClearTheOutput(string? token)
    {
        CoreLogLevel? severity = CoreLogLevel.Critical;
        ConfigurationTokenState state = SeverityTokenParser.Parse(token, out severity);

        Assert.AreEqual(ConfigurationTokenState.Absent, state);
        Assert.IsNull(severity);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("1")]
    [DataRow("2")]
    [DataRow("3")]
    [DataRow("4")]
    [DataRow("5")]
    [DataRow("6")]
    [DataRow("7")]
    [DataRow("-1")]
    [DataRow("+2")]
    [DataRow("Fatal")]
    [DataRow("Trace,Debug")]
    [DataRow("Information Warning")]
    [DataRow("unknown")]
    [DataRow(" Error! ")]
    [DataRow("Informa tion")]
    [DataRow("Truce")]
    [DataRow("Debag")]
    [DataRow("InformatioX")]
    [DataRow("WarninX")]
    [DataRow("ErroX")]
    [DataRow("CriticaX")]
    [DataRow("NonX")]
    [DataRow("IN")]
    [DataRow("ER")]
    [DataRow("CR")]
    [DataRow("DEB")]
    [DataRow("OFF")]
    [DataRow("TRX")]
    [DataRow("DBX")]
    [DataRow("INX")]
    [DataRow("WRX")]
    [DataRow("ERX")]
    [DataRow("CRX")]
    [DataRow("INFX")]
    [DataRow("WARX")]
    [DataRow("CRIX")]
    [DataRow("XRC")]
    [DataRow("XBG")]
    [DataRow("XNF")]
    [DataRow("XRN")]
    [DataRow("XRR")]
    [DataRow("XRT")]
    [DataRow("XONE")]
    [DataRow("XINFO")]
    [DataRow("XARN")]
    [DataRow("XRIT")]
    public void PresentUnrecognizedTokensAreInvalidAndClearTheOutput(string token)
    {
        CoreLogLevel? severity = CoreLogLevel.Critical;
        ConfigurationTokenState state = SeverityTokenParser.Parse(token, out severity);

        Assert.AreEqual(ConfigurationTokenState.Invalid, state);
        Assert.IsNull(severity);
    }

    [TestMethod]
    [DataRow("tr-TR")]
    [DataRow("az-Latn-AZ")]
    public void TokenRecognitionIsIndependentOfCurrentCulture(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            ConfigurationTokenState state = SeverityTokenParser.Parse("information", out CoreLogLevel? severity);

            Assert.AreEqual(ConfigurationTokenState.Valid, state);
            Assert.AreEqual(CoreLogLevel.Information, severity);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
