// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using ViewfinderHeadTracking.Camera;
using Xunit;

namespace ViewfinderHeadTracking.Tests;

public class ZoomCompensationTests
{
    [Fact]
    public void FactorIsTheRatioOfTheTangents()
    {
        Approx.Equal(0.5f, ZoomCompensation.Factor(1f, 2f));
        Approx.Equal(1f, ZoomCompensation.Factor(0.6745f, 0.6745f));
    }

    [Fact]
    public void UnitFactorLeavesAnAngleAlone()
    {
        Approx.Equal(45f, ZoomCompensation.ScaleAngle(45f, 1f));
        Approx.Equal(-12.5f, ZoomCompensation.ScaleAngle(-12.5f, 1f));
    }

    [Fact]
    public void AngleScalesThroughItsTangent()
    {
        Approx.Equal(26.56505f, ZoomCompensation.ScaleAngle(45f, 0.5f));
        Approx.Equal(0f, ZoomCompensation.ScaleAngle(0f, 0.3f));
    }

    [Fact]
    public void OnlyAFinitePositiveTangentIsUsable()
    {
        Assert.True(ZoomCompensation.IsUsableTangent(0.6745f));
        Assert.False(ZoomCompensation.IsUsableTangent(0f));
        Assert.False(ZoomCompensation.IsUsableTangent(-0.5f));
        Assert.False(ZoomCompensation.IsUsableTangent(float.NaN));
        Assert.False(ZoomCompensation.IsUsableTangent(float.PositiveInfinity));
    }

    [Fact]
    public void AnglePastThePoleIsHeldInsideIt()
    {
        Approx.Equal(89.9f, ZoomCompensation.ScaleAngle(100f, 1f));
        Approx.Equal(-89.9f, ZoomCompensation.ScaleAngle(-100f, 1f));
    }
}
