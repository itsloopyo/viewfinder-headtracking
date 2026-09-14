// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using ViewfinderHeadTracking.Tracking;
using Xunit;

namespace ViewfinderHeadTracking.Tests;

public class TrackerConnectionMonitorTests
{
    [Fact]
    public void FirstConnectionIsLeftToTheReceiver()
    {
        var monitor = new TrackerConnectionMonitor();
        Assert.Null(monitor.Poll(false));
        Assert.Null(monitor.Poll(true));
        Assert.Null(monitor.Poll(true));
    }

    [Fact]
    public void LaterChangesAreReportedOncePerChange()
    {
        var monitor = new TrackerConnectionMonitor();
        monitor.Poll(true);

        Assert.Equal("Tracker data stopped - nothing has arrived on the UDP port for five seconds", monitor.Poll(false));
        Assert.Null(monitor.Poll(false));
        Assert.Equal("Tracker data resumed", monitor.Poll(true));
        Assert.Null(monitor.Poll(true));
    }
}
