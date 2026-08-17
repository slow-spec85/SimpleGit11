using System;

namespace SimpleGit11.Presentation.Navigation;

public sealed class WindowActivationRefreshGate
{
    private readonly TimeSpan _minimumInactiveDuration;
    private readonly TimeSpan _refreshCooldown;
    private readonly TimeProvider _timeProvider;
    private bool _hasBeenActivated;
    private long? _deactivatedTimestamp;
    private long? _lastRefreshTimestamp;

    public WindowActivationRefreshGate(
        TimeSpan minimumInactiveDuration,
        TimeSpan refreshCooldown,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumInactiveDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(refreshCooldown, TimeSpan.Zero);

        _minimumInactiveDuration = minimumInactiveDuration;
        _refreshCooldown = refreshCooldown;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool OnActivationChanged(bool isActive)
    {
        if (!isActive)
        {
            if (_hasBeenActivated && _deactivatedTimestamp is null)
            {
                _deactivatedTimestamp = _timeProvider.GetTimestamp();
            }

            return false;
        }

        if (!_hasBeenActivated)
        {
            _hasBeenActivated = true;
            _deactivatedTimestamp = null;
            return false;
        }

        if (_deactivatedTimestamp is not long deactivatedTimestamp)
        {
            return false;
        }

        long activatedTimestamp = _timeProvider.GetTimestamp();
        _deactivatedTimestamp = null;

        if (_timeProvider.GetElapsedTime(deactivatedTimestamp, activatedTimestamp) < _minimumInactiveDuration)
        {
            return false;
        }

        if (_lastRefreshTimestamp is long lastRefreshTimestamp
            && _timeProvider.GetElapsedTime(lastRefreshTimestamp, activatedTimestamp) < _refreshCooldown)
        {
            return false;
        }

        _lastRefreshTimestamp = activatedTimestamp;
        return true;
    }
}
