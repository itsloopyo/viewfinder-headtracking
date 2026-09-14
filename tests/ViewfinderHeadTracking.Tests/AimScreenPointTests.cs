// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;
using ViewfinderHeadTracking.Camera;
using Xunit;

namespace ViewfinderHeadTracking.Tests;

public class AimScreenPointTests
{
    private const float Width = 1280f;
    private const float Height = 720f;

    [Fact]
    public void EdgePointPushesAlongTheDominantAxis()
    {
        Approx.Equal(new Vector2(1920f, 0f), AimScreenPoint.EdgePoint(new Vector3(0.2f, -0.1f, -1f), Width, Height));
        Approx.Equal(new Vector2(0f, 1080f), AimScreenPoint.EdgePoint(new Vector3(-0.1f, 0.2f, -1f), Width, Height));
    }

    [Fact]
    public void EdgePointOnTheViewAxisStaysAtCentre()
    {
        Approx.Equal(new Vector2(640f, 360f), AimScreenPoint.EdgePoint(new Vector3(0f, 0f, -1f), Width, Height));
    }

    [Fact]
    public void EdgePointStraightBehindDoesNotFlipWithNoise()
    {
        // A forward lean past a surface 0.3 m ahead: the point sits 0.1 m behind the
        // eye, and tracker noise moves it a fraction of a millimetre either side.
        Approx.Equal(new Vector2(640f, 360f), AimScreenPoint.EdgePoint(new Vector3(5e-4f, 0f, -0.1f), Width, Height));
        Approx.Equal(new Vector2(640f, 360f), AimScreenPoint.EdgePoint(new Vector3(-5e-4f, 0f, -0.1f), Width, Height));
    }

    [Fact]
    public void ClampKeepsTheReticleInsideTheEdgeMargin()
    {
        Approx.Equal(new Vector2(24f, 696f), AimScreenPoint.ClampToScreen(new Vector2(-5f, 800f), Width, Height));
        Approx.Equal(new Vector2(1256f, 24f), AimScreenPoint.ClampToScreen(new Vector2(1920f, 0f), Width, Height));
        Approx.Equal(new Vector2(700f, 300f), AimScreenPoint.ClampToScreen(new Vector2(700f, 300f), Width, Height));
    }
}
