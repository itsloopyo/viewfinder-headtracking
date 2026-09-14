// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using ViewfinderHeadTracking.Tracking;
using Xunit;

namespace ViewfinderHeadTracking.Tests;

public sealed class TrackingPipelineTests : IDisposable
{
    private const float FrameTime = 1f / 60f;

    private readonly OpenTrackReceiver _receiver = new();
    private readonly UdpClient _sender = new();
    private readonly IPEndPoint _target;
    private readonly TrackingPipeline _pipeline;

    public TrackingPipelineTests()
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }
        Assert.True(_receiver.Start(port));
        _target = new IPEndPoint(IPAddress.Loopback, port);

        _pipeline = new TrackingPipeline(
            _receiver,
            new TrackingProcessor
            {
                LocalSmoothing = 0f,
                RemoteSmoothing = 0.15f,
                Sensitivity = SensitivitySettings.Default,
                Deadzone = DeadzoneSettings.None
            },
            new PoseInterpolator(),
            new PositionProcessor
            {
                Settings = new PositionSettings(1f, 1f, 1f, 0.30f, 0.20f, 0.20f, 0.40f, 0.10f,
                    localSmoothing: 0f, remoteSmoothing: 0.15f, invertX: false, invertY: false, invertZ: false)
            },
            new PositionInterpolator());
    }

    public void Dispose()
    {
        _sender.Dispose();
        _receiver.Dispose();
    }

    [Fact]
    public void OverflowingAnglesDoNotLatchTheViewToNaN()
    {
        Send(yaw: 5.0, pitch: 10.0, roll: 2.0);
        AssertFiniteFor(frames: 5);

        // Each value is a finite float, so the receiver accepts both datagrams, but the
        // interpolator's segment between them overflows.
        Send(yaw: 0.0, pitch: 3e38, roll: 0.0);
        AssertFiniteFor(frames: 3);
        Send(yaw: 0.0, pitch: -3e38, roll: 0.0);
        AssertFiniteFor(frames: 3);

        Send(yaw: 5.0, pitch: 10.0, roll: 2.0);
        HeadPose pose = AssertFiniteFor(frames: 120);
        Assert.True(_pipeline.IsApplying);
        Assert.InRange(pose.Yaw, 5f - 0.01f, 5f + 0.01f);
        Assert.InRange(pose.Pitch, 10f - 0.01f, 10f + 0.01f);
        Assert.InRange(pose.Roll, 2f - 0.01f, 2f + 0.01f);
        Assert.True(_pipeline.RejectedPoses > 0);
    }

    [Fact]
    public void AnOrdinaryStreamRejectsNothing()
    {
        for (int i = 0; i < 10; i++)
        {
            Send(yaw: i, pitch: -i, roll: 0.5 * i, x: 1.0, y: -2.0, z: 3.0);
            AssertFiniteFor(frames: 2);
        }

        Assert.Equal(0, _pipeline.RejectedPoses);
    }

    [Fact]
    public void ResumingDuringTheFadeOutCarriesOnFromTheViewOnScreen()
    {
        Send(yaw: 20.0, pitch: 0.0, roll: 0.0);
        HeadPose full = AssertFiniteFor(frames: 60);
        Assert.InRange(full.Yaw, 19.99f, 20.01f);

        HeadPose onScreen = full;
        for (int i = 0; i < 10; i++)
        {
            onScreen = _pipeline.ProcessFrame(false, FrameTime, FrameTime);
        }
        Assert.InRange(onScreen.Yaw, 5f, 15f);

        // A gate that closes and reopens inside the fade out, or End pressed twice.
        float previous = onScreen.Yaw;
        for (int i = 0; i < 60; i++)
        {
            HeadPose pose = _pipeline.ProcessFrame(true, FrameTime, FrameTime);
            Assert.True(Math.Abs(pose.Yaw - previous) < 1.5f,
                $"frame {i}: view stepped from {previous} to {pose.Yaw} degrees");
            previous = pose.Yaw;
        }
        Assert.InRange(previous, 19.99f, 20.01f);
    }

    private HeadPose AssertFiniteFor(int frames)
    {
        HeadPose pose = HeadPose.Zero;
        for (int i = 0; i < frames; i++)
        {
            pose = _pipeline.ProcessFrame(true, FrameTime, FrameTime);
            Assert.True(pose.IsFinite,
                $"frame {i}: yaw {pose.Yaw} pitch {pose.Pitch} roll {pose.Roll} " +
                $"x {pose.Position.X} y {pose.Position.Y} z {pose.Position.Z}");
        }
        return pose;
    }

    /// <summary>Sends one OpenTrack datagram and waits until the receiver has published it.</summary>
    private void Send(double yaw, double pitch, double roll, double x = 0.0, double y = 0.0, double z = 0.0)
    {
        var packet = new byte[OpenTrackPacket.MinPacketSize];
        double[] fields = { x, y, z, yaw, pitch, roll };
        for (int i = 0; i < fields.Length; i++)
        {
            BitConverter.GetBytes(fields[i]).CopyTo(packet, i * 8);
        }

        long before = _receiver.GetRawPose().TimestampTicks;
        _sender.Send(packet, packet.Length, _target);

        var clock = Stopwatch.StartNew();
        while (_receiver.GetRawPose().TimestampTicks == before)
        {
            if (clock.ElapsedMilliseconds > 5000) throw new TimeoutException("The receiver never published the datagram");
            Thread.Sleep(1);
        }
    }
}
