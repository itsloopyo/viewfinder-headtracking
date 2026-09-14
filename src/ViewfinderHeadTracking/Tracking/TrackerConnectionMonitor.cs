// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

namespace ViewfinderHeadTracking.Tracking;

/// <summary>
/// Turns the receiver's receiving flag into one log line per real change: the
/// tracker going quiet and coming back. The first connection is not reported here,
/// because the receiver names its endpoint itself.
/// </summary>
internal sealed class TrackerConnectionMonitor
{
    private bool _receiving;
    private bool _seenData;

    /// <summary>The line to log for this poll, or null when there is nothing to report.</summary>
    internal string? Poll(bool receiving)
    {
        if (receiving == _receiving) return null;
        _receiving = receiving;

        if (!_seenData)
        {
            _seenData = true;
            return null;
        }

        return receiving
            ? "Tracker data resumed"
            : "Tracker data stopped - nothing has arrived on the UDP port for five seconds";
    }
}
