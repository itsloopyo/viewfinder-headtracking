// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using CameraUnlock.Core.Data;
using UnityEngine;
using ViewfinderHeadTracking.Tracking;
using Xunit;

namespace ViewfinderHeadTracking.Tests;

public class HeadPoseTests
{
    private static readonly float Half = Mathf.Sqrt(0.5f);

    // Built from components: Quaternion.Euler and AngleAxis are native externs.
    private static readonly Quaternion Yaw90 = new(0f, Half, 0f, Half);
    private static readonly Quaternion LookingDown = new(Half, 0f, 0f, Half);
    private static readonly Quaternion LookingUp = new(-Half, 0f, 0f, Half);

    private static HeadPose Pose(float yaw, float pitch, float roll, float x, float y, float z)
    {
        return new HeadPose { Yaw = yaw, Pitch = pitch, Roll = roll, Position = new Vec3(x, y, z) };
    }

    [Fact]
    public void EngineBoundaryMirrorsPitchXAndZ()
    {
        HeadPose pose = Pose(10f, 20f, 30f, 0.1f, 0.2f, -0.3f);
        Approx.Equal(10f, pose.EngineYaw);
        Approx.Equal(-20f, pose.EnginePitch);
        Approx.Equal(30f, pose.EngineRoll);
        Approx.Equal(new Vector3(-0.1f, 0.2f, 0.3f), pose.EngineOffset);
    }

    [Fact]
    public void ScaledScalesEveryAxis()
    {
        HeadPose scaled = Pose(10f, -20f, 30f, 0.1f, 0.2f, -0.4f).Scaled(0.5f);
        Approx.Equal(5f, scaled.Yaw);
        Approx.Equal(-10f, scaled.Pitch);
        Approx.Equal(15f, scaled.Roll);
        Approx.Equal(0.05f, scaled.Position.X);
        Approx.Equal(0.1f, scaled.Position.Y);
        Approx.Equal(-0.2f, scaled.Position.Z);
    }

    [Fact]
    public void IsFiniteChecksEveryAxis()
    {
        Assert.True(Pose(10f, -20f, 30f, 0.1f, 0.2f, -0.3f).IsFinite);
        Assert.False(Pose(float.NaN, 0f, 0f, 0f, 0f, 0f).IsFinite);
        Assert.False(Pose(0f, float.NegativeInfinity, 0f, 0f, 0f, 0f).IsFinite);
        Assert.False(Pose(0f, 0f, float.NaN, 0f, 0f, 0f).IsFinite);
        Assert.False(Pose(0f, 0f, 0f, float.PositiveInfinity, 0f, 0f).IsFinite);
        Assert.False(Pose(0f, 0f, 0f, 0f, float.NaN, 0f).IsFinite);
        Assert.False(Pose(0f, 0f, 0f, 0f, 0f, float.NaN).IsFinite);
    }

    [Fact]
    public void LerpBlendsEveryAxis()
    {
        HeadPose blended = HeadPose.Lerp(Pose(10f, 20f, 30f, 0.2f, 0.4f, -0.6f), HeadPose.Zero, 0.25f);
        Approx.Equal(7.5f, blended.Yaw);
        Approx.Equal(15f, blended.Pitch);
        Approx.Equal(22.5f, blended.Roll);
        Approx.Equal(0.15f, blended.Position.X);
        Approx.Equal(0.3f, blended.Position.Y);
        Approx.Equal(-0.45f, blended.Position.Z);
    }

    [Fact]
    public void NoOffsetIsNoLean()
    {
        Assert.Equal(Vector3.zero, Pose(10f, 20f, 30f, 0f, 0f, 0f).LeanWorld(Yaw90));
    }

    [Fact]
    public void LeanFollowsTheCleanHeading()
    {
        HeadPose pose = Pose(0f, 0f, 0f, 0.1f, 0.2f, -0.3f);
        Approx.Equal(new Vector3(-0.1f, 0.2f, 0.3f), pose.LeanWorld(Quaternion.identity));
        Approx.Equal(new Vector3(0.3f, 0.2f, 0.1f), pose.LeanWorld(Yaw90));
    }

    [Fact]
    public void LeanStaysHorizonLockedLookingStraightUpOrDown()
    {
        HeadPose pose = Pose(0f, 0f, 0f, 0.1f, 0.2f, -0.3f);
        Approx.Equal(new Vector3(-0.1f, 0.2f, 0.3f), pose.LeanWorld(LookingDown));
        Approx.Equal(new Vector3(-0.1f, 0.2f, 0.3f), pose.LeanWorld(LookingUp));
    }

    // The relationships a scripted sweep measured in game: yaw +20 turned the view right
    // by sin 20, pitch +15 up by sin 15, and roll +25 tilted the top of the view left.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RotationSignsMatchTheMeasuredSweep(bool worldSpaceYaw)
    {
        Quaternion yaw = Pose(20f, 0f, 0f, 0f, 0f, 0f).ComposeRotation(Quaternion.identity, worldSpaceYaw);
        Approx.Equal(0.34202f, Vector3.Dot(yaw * Vector3.forward, Vector3.right));

        Quaternion pitch = Pose(0f, 15f, 0f, 0f, 0f, 0f).ComposeRotation(Quaternion.identity, worldSpaceYaw);
        Approx.Equal(0.25882f, Vector3.Dot(pitch * Vector3.forward, Vector3.up));

        Quaternion roll = Pose(0f, 0f, 25f, 0f, 0f, 0f).ComposeRotation(Quaternion.identity, worldSpaceYaw);
        Approx.Equal(-0.42262f, Vector3.Dot(roll * Vector3.up, Vector3.right));
    }

    [Fact]
    public void WorldSpaceYawSpinsAboutWorldUpWhileLookingDown()
    {
        HeadPose pose = Pose(20f, 0f, 0f, 0f, 0f, 0f);
        Approx.Equal(new Vector3(0f, -1f, 0f), pose.ComposeRotation(LookingDown, true) * Vector3.forward);
        Approx.Equal(new Vector3(0.34202f, -0.93969f, 0f), pose.ComposeRotation(LookingDown, false) * Vector3.forward);
    }

    [Fact]
    public void UnitZoomOnlyAppliesTheSafetyLimits()
    {
        HeadPose limited = Pose(30f, 80f, 60f, 0.1f, 0.2f, -0.3f).WithZoomAndLimits(1f);
        Approx.Equal(30f, limited.Yaw);
        Approx.Equal(70f, limited.Pitch);
        Approx.Equal(45f, limited.Roll);
        Approx.Equal(0.1f, limited.Position.X);
        Approx.Equal(0.2f, limited.Position.Y);
        Approx.Equal(-0.3f, limited.Position.Z);

        HeadPose negative = Pose(-100f, -80f, -60f, 0f, 0f, 0f).WithZoomAndLimits(1f);
        Approx.Equal(-89.9f, negative.Yaw);
        Approx.Equal(-70f, negative.Pitch);
        Approx.Equal(-45f, negative.Roll);
    }

    [Fact]
    public void ZoomScalesYawPitchAndLeanButNotRoll()
    {
        HeadPose zoomed = Pose(30f, 60f, 20f, 0.1f, 0.2f, -0.3f).WithZoomAndLimits(0.5f);
        Approx.Equal(16.10211f, zoomed.Yaw);
        Approx.Equal(40.89339f, zoomed.Pitch);
        Approx.Equal(20f, zoomed.Roll);
        Approx.Equal(0.05f, zoomed.Position.X);
        Approx.Equal(0.1f, zoomed.Position.Y);
        Approx.Equal(-0.15f, zoomed.Position.Z);
    }

    [Fact]
    public void PitchIsLimitedAfterItIsScaled()
    {
        Approx.Equal(70f, Pose(0f, 85f, 0f, 0f, 0f, 0f).WithZoomAndLimits(0.5f).Pitch);
    }
}
