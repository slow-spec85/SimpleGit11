using System;
using SimpleGit11.Presentation.Navigation;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class WindowActivationRefreshGateTests
{
    private static readonly TimeSpan MinimumInactiveDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void OnActivationChanged_InitialActivation_DoesNotRefresh()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        bool shouldRefresh = gate.OnActivationChanged(isActive: true);

        Assert.IsFalse(shouldRefresh);
    }

    [TestMethod]
    public void OnActivationChanged_DeactivationBeforeInitialActivation_DoesNotRefresh()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(MinimumInactiveDuration);
        bool shouldRefresh = gate.OnActivationChanged(isActive: true);

        Assert.IsFalse(shouldRefresh);
    }

    [TestMethod]
    public void OnActivationChanged_InactiveForLessThanMinimum_DoesNotRefresh()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        _ = gate.OnActivationChanged(isActive: true);
        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(MinimumInactiveDuration - TimeSpan.FromMilliseconds(1));

        bool shouldRefresh = gate.OnActivationChanged(isActive: true);

        Assert.IsFalse(shouldRefresh);
    }

    [TestMethod]
    public void OnActivationChanged_InactiveForMinimum_RefreshesOnce()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        _ = gate.OnActivationChanged(isActive: true);
        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(MinimumInactiveDuration);

        bool firstActivation = gate.OnActivationChanged(isActive: true);
        bool repeatedActivation = gate.OnActivationChanged(isActive: true);

        Assert.IsTrue(firstActivation);
        Assert.IsFalse(repeatedActivation);
    }

    [TestMethod]
    public void OnActivationChanged_RefreshWithinCooldown_DoesNotRefresh()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        _ = gate.OnActivationChanged(isActive: true);
        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(MinimumInactiveDuration);
        Assert.IsTrue(gate.OnActivationChanged(isActive: true));

        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(MinimumInactiveDuration);
        bool shouldRefresh = gate.OnActivationChanged(isActive: true);

        Assert.IsFalse(shouldRefresh);
    }

    [TestMethod]
    public void OnActivationChanged_RefreshAfterCooldown_Refreshes()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        _ = gate.OnActivationChanged(isActive: true);
        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(MinimumInactiveDuration);
        Assert.IsTrue(gate.OnActivationChanged(isActive: true));

        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(RefreshCooldown);
        bool shouldRefresh = gate.OnActivationChanged(isActive: true);

        Assert.IsTrue(shouldRefresh);
    }

    [TestMethod]
    public void OnActivationChanged_RepeatedDeactivation_DoesNotRestartInactivePeriod()
    {
        ManualTimeProvider timeProvider = new();
        WindowActivationRefreshGate gate = CreateGate(timeProvider);

        _ = gate.OnActivationChanged(isActive: true);
        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        _ = gate.OnActivationChanged(isActive: false);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        bool shouldRefresh = gate.OnActivationChanged(isActive: true);

        Assert.IsTrue(shouldRefresh);
    }

    private static WindowActivationRefreshGate CreateGate(TimeProvider timeProvider)
    {
        return new WindowActivationRefreshGate(
            MinimumInactiveDuration,
            RefreshCooldown,
            timeProvider);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
        }
    }
}
