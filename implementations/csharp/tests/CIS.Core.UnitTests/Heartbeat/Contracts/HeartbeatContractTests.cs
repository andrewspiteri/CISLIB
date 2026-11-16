using System.Reflection;
using CIS.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatContractTests
{
    [TestMethod]
    public void ConsumerServiceHasOnlyInstanceRegistrationAndAwaitedUnregistration()
    {
        Type service = typeof(IHeartbeatService);
        CollectionAssert.AreEquivalent(new[] { "Register", "UnregisterAsync" }, service.GetMethods().Select(method => method.Name).ToArray());
        MethodInfo register = service.GetMethod("Register")!;
        Assert.AreEqual(typeof(void), register.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(IHeartbeatSubscriber), typeof(HeartbeatRegistrationOptions), typeof(Func<CancellationToken, Task<bool>>) },
            register.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.IsTrue(register.GetParameters()[2].HasDefaultValue);
        Assert.IsNull(register.GetParameters()[2].DefaultValue);
        MethodInfo unregister = service.GetMethod("UnregisterAsync")!;
        Assert.AreEqual(typeof(Task), unregister.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(IHeartbeatSubscriber), typeof(CancellationToken) },
            unregister.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.IsTrue(unregister.GetParameters()[1].HasDefaultValue);
        Assert.AreEqual("CIS.Core", service.Assembly.GetName().Name);
        Assert.AreEqual("CIS.Heartbeat", service.Namespace);
    }

    [TestMethod]
    public void SubscriberCallbacksExposeTheWholeAwaitableWorkBoundary()
    {
        MethodInfo elapsed = typeof(IHeartbeatSubscriber).GetMethod("OnElapsedAsync")!;
        Assert.AreEqual(typeof(Task<bool>), elapsed.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(CancellationToken) }, elapsed.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        MethodInfo error = typeof(IHeartbeatSubscriber).GetMethod("OnErrorAsync")!;
        Assert.AreEqual(typeof(Task), error.ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(Exception) }, error.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.AreEqual(2, typeof(IHeartbeatSubscriber).GetMethods().Length);
    }

    [TestMethod]
    public void ScheduleDefaultsAndCopiedValuesHaveNoHealthOrLifecyclePolicy()
    {
        var options = new HeartbeatRegistrationOptions();
        Assert.AreEqual(TimeSpan.FromMilliseconds(10), options.Interval);
        Assert.AreEqual(TimeSpan.Zero, options.InitialDelay);
        Assert.IsNull(options.Name);
        HeartbeatRegistrationOptions copy = options.ValidatedCopy();
        Assert.AreNotSame(options, copy);
        Assert.AreEqual(options.Interval, copy.Interval);
        Assert.AreEqual(options.InitialDelay, copy.InitialDelay);
        CollectionAssert.AreEquivalent(new[] { "MaximumDuration", "Interval", "InitialDelay", "Name" },
            typeof(HeartbeatRegistrationOptions).GetProperties().Select(property => property.Name).ToArray());
        Assert.IsTrue(typeof(HeartbeatRegistrationOptions).IsSealed);
    }

    [TestMethod]
    public void DocumentedDurationEndpointsAreAcceptedWithoutRoundingTheInput()
    {
        Assert.AreEqual(TimeSpan.FromMilliseconds(4_294_967_294), HeartbeatRegistrationOptions.MaximumDuration);
        HeartbeatRegistrationOptions minimum = new HeartbeatRegistrationOptions { Interval = TimeSpan.FromTicks(1) }.ValidatedCopy();
        HeartbeatRegistrationOptions maximum = new HeartbeatRegistrationOptions
        {
            Interval = HeartbeatRegistrationOptions.MaximumDuration,
            InitialDelay = HeartbeatRegistrationOptions.MaximumDuration
        }.ValidatedCopy();
        Assert.AreEqual(TimeSpan.FromTicks(1), minimum.Interval);
        Assert.AreEqual(HeartbeatRegistrationOptions.MaximumDuration, maximum.Interval);
        Assert.AreEqual(HeartbeatRegistrationOptions.MaximumDuration, maximum.InitialDelay);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(long.MinValue)]
    [DataRow(long.MaxValue)]
    public void InvalidIntervalsAreRejectedBeforeAnyRegistrationCanBePublished(long ticks)
    {
        ArgumentOutOfRangeException failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new HeartbeatRegistrationOptions { Interval = TimeSpan.FromTicks(ticks) }.ValidatedCopy());
        Assert.AreEqual("Interval", failure.ParamName);
    }

    [TestMethod]
    public void OneTickBeyondEachDocumentedDurationBoundIsRejected()
    {
        TimeSpan beyond = HeartbeatRegistrationOptions.MaximumDuration + TimeSpan.FromTicks(1);
        Assert.AreEqual("Interval", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new HeartbeatRegistrationOptions { Interval = beyond }.ValidatedCopy()).ParamName);
        Assert.AreEqual("InitialDelay", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new HeartbeatRegistrationOptions { InitialDelay = beyond }.ValidatedCopy()).ParamName);
        Assert.AreEqual("InitialDelay", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new HeartbeatRegistrationOptions { InitialDelay = TimeSpan.FromTicks(-1) }.ValidatedCopy()).ParamName);
    }

    [TestMethod]
    public void DiagnosticNameLimitCountsUnicodeScalarsAndCopiesNamesWithoutNormalizingThem()
    {
        string name = string.Concat(Enumerable.Repeat("\U0001F49A", 128));
        HeartbeatRegistrationOptions copy = new HeartbeatRegistrationOptions { Name = name }.ValidatedCopy();
        Assert.AreEqual(name, copy.Name);
        Assert.AreEqual(256, copy.Name!.Length);
        Assert.AreEqual(string.Empty, new HeartbeatRegistrationOptions { Name = string.Empty }.ValidatedCopy().Name);
        Assert.AreEqual(" name\n", new HeartbeatRegistrationOptions { Name = " name\n" }.ValidatedCopy().Name);
        Assert.ThrowsExactly<ArgumentException>(() => new HeartbeatRegistrationOptions { Name = name + "x" }.ValidatedCopy());
        Assert.ThrowsExactly<ArgumentException>(() => new HeartbeatRegistrationOptions { Name = "\ud800" }.ValidatedCopy());
        Assert.ThrowsExactly<ArgumentException>(() => new HeartbeatRegistrationOptions { Name = "\udc00" }.ValidatedCopy());
    }
}
