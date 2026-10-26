using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUtcObservation;

namespace CIS.Tests.ArchitectureTests;

// Receiving controls preserve the actual marker text and exact ns chronology.
[TestClass]
public sealed class HealthcheckMutationUtcObservationTests
{
    [TestMethod]
    public void ActualOriginalPipesMarkerRetainsItsCommaAndAllNineFractionalDigits()
    {
        const string raw = "2026-10-22T11:37:10,654533965+00:00";
        Assert.IsTrue(TryRead(raw, out Observation result));
        Assert.AreEqual(raw, result.Raw);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 22, 11, 37, 10, TimeSpan.Zero).UtcTicks + 6545339, result.UtcTicks);
        Assert.AreEqual((byte)65, result.SubTickNanoseconds);
    }

    [TestMethod]
    [DataRow("2026-10-22T11:37:10Z")]
    [DataRow("2026-10-22T11:37:10+00:00")]
    [DataRow("2026-10-22T11:37:10.6545339+00:00")]
    [DataRow("2026-10-22T13:37:10.6545339+02:00")]
    public void ExistingExplicitFormatsRemainAcceptedWithoutNormalizingTheirRawSpelling(string raw)
    {
        Assert.IsTrue(TryRead(raw, out Observation observed));
        Assert.AreEqual(raw, observed.Raw);
        Assert.AreEqual((byte)0, observed.SubTickNanoseconds);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("2026-10-22T11:37:10,65453396+00:00")]
    [DataRow("2026-10-22T11:37:10,6545339650+00:00")]
    [DataRow("2026-10-22T11:37:10.654533965+00:00")]
    [DataRow("2026-10-22T11:37:10,654533965Z")]
    [DataRow("2026-10-22T11:37:10,654533965+01:00")]
    [DataRow("2026-10-22T11:37:10,654533965-00:00")]
    [DataRow("2026-10-22T11:37:10,654533965+00:60")]
    [DataRow("2026-02-30T11:37:10,654533965+00:00")]
    [DataRow("2026-10-22T11:37:60,654533965+00:00")]
    [DataRow("2026-10-22T11:37:10,65453396x+00:00")]
    [DataRow(" 2026-10-22T11:37:10,654533965+00:00")]
    [DataRow("2026-10-22T11:37:10,654533965+00:00 suffix")]
    public void UnsupportedOrMalformedNativeSourceSpellingIsRejected(string raw) => Assert.IsFalse(TryRead(raw, out _));

    [TestMethod]
    [DataRow("2026-10-22T11:37:10,654533964+00:00", "2026-10-22T11:37:10,654533965+00:00", -1)]
    [DataRow("2026-10-22T11:37:10,654533965+00:00", "2026-10-22T11:37:10,654533965+00:00", 0)]
    [DataRow("2026-10-22T11:37:10,654533966+00:00", "2026-10-22T11:37:10,654533965+00:00", 1)]
    [DataRow("2026-10-22T11:37:10,999999999+00:00", "2026-10-22T11:37:11,000000000+00:00", -1)]
    public void SubTickNanosecondsAndSecondRolloverRemainStrictlyOrdered(string left, string right, int expectedSign)
    {
        Assert.IsTrue(TryRead(left, out Observation first));
        Assert.IsTrue(TryRead(right, out Observation second));
        Assert.AreEqual(expectedSign, Math.Sign(first.CompareTo(second)));
    }

    [TestMethod]
    [DataRow("2026-10-22T11:37:10.6545339+00:00", false)]
    [DataRow("2026-10-22T11:37:10.6545340+00:00", true)]
    [DataRow("2026-10-22T11:37:10.6545338+00:00", false)]
    public void ActualMarkerMustStrictlyPrecedeTheTrxStartWithoutRoundingDownIntoAnApproval(string freshText, bool expected)
    {
        Assert.IsTrue(TryRead("2026-10-22T11:37:10,654533965+00:00", out Observation marker));
        DateTimeOffset fresh = DateTimeOffset.ParseExact(freshText, "O", CultureInfo.InvariantCulture);
        Assert.AreEqual(expected, marker.IsBefore(fresh));
    }

    [TestMethod]
    public void EqualWholeTickLegacyObservationIsNotStrictlyBeforeTheFreshStart()
    {
        Assert.IsTrue(TryRead("2026-10-22T11:37:10.6545339+00:00", out Observation marker));
        Assert.IsFalse(marker.IsBefore(DateTimeOffset.ParseExact(marker.Raw, "O", CultureInfo.InvariantCulture)));
    }
}
