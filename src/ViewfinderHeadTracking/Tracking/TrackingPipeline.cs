// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Math;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;

namespace ViewfinderHeadTracking.Tracking;

/// <summary>
/// Runs the shared pipeline - receiver, interpolator, processor - and produces one
/// <see cref="HeadPose"/> per frame, with a fade in when tracking starts and a fade
/// out when it stops.
///
/// Nothing here keeps a centre. Every tracker in use centres itself, and a second
/// centre in series with the tracker's own drifts apart from it. The pose that
/// arrives is the pose that is applied.
/// </summary>
internal sealed class TrackingPipeline
{
    private const float TransitionInDuration = 0.5f;
    private const float TransitionOutDuration = 0.3f;

    private readonly OpenTrackReceiver _receiver;
    private readonly TrackingProcessor _processor;
    private readonly PoseInterpolator _interpolator;
    private readonly PositionProcessor _positionProcessor;
    private readonly PositionInterpolator _positionInterpolator;

    private HeadPose _lastAppliedPose = HeadPose.Zero;
    private bool _wasApplying;
    private bool _isTransitioningIn;
    private float _transitionInProgress;
    private bool _isTransitioningOut;
    private float _transitionOutProgress;

    // Position stays off until the tracker delivers a non-zero position sample, so a
    // rotation-only tracker never picks up an offset.
    private bool _detected6Dof;

    internal TrackingPipeline(OpenTrackReceiver receiver, TrackingProcessor processor,
        PoseInterpolator interpolator, PositionProcessor positionProcessor,
        PositionInterpolator positionInterpolator)
    {
        _receiver = receiver;
        _processor = processor;
        _interpolator = interpolator;
        _positionProcessor = positionProcessor;
        _positionInterpolator = positionInterpolator;
    }

    internal bool RotationEnabled { get; set; } = true;
    internal bool PositionEnabled { get; set; } = true;

    /// <summary>Whether a pose is reaching the camera this frame.</summary>
    internal bool IsApplying { get; private set; }

    internal bool IsRemoteConnection { get; private set; }

    internal int RejectedPoses { get; private set; }

    /// <summary>
    /// Advances one frame and returns the pose to draw with. <see cref="IsApplying"/>
    /// is false, and the pose zero, when nothing should be applied.
    /// </summary>
    internal HeadPose ProcessFrame(bool enabled, float deltaTime, float unscaledDeltaTime)
    {
        if (enabled && _receiver.IsReceiving)
        {
            if (_isTransitioningOut) ResumeFromFadeOut();

            // Re-read every frame: the smoothing parameter is chosen per connection,
            // so swapping a local tracker for a phone takes effect without a restart.
            IsRemoteConnection = _receiver.IsRemoteConnection;
            _processor.IsRemoteConnection = IsRemoteConnection;
            _positionProcessor.IsRemoteConnection = IsRemoteConnection;

            if (!_wasApplying) BeginSession();

            float scale = AdvanceTransitionIn(deltaTime);

            TrackingPose raw = _receiver.GetLatestPose();
            TrackingPose interpolated = _interpolator.Update(raw, deltaTime);
            TrackingPose processed = _processor.Process(interpolated, deltaTime);

            var pose = new HeadPose
            {
                Yaw = RotationEnabled ? processed.Yaw : 0f,
                Pitch = RotationEnabled ? processed.Pitch : 0f,
                Roll = RotationEnabled ? processed.Roll : 0f,
                Position = ComputePosition(deltaTime)
            };

            // The receiver only checks that each value is a finite float, so two
            // datagrams at +/-3e38 degrees pass it and overflow the interpolator's
            // segment between them. The resulting NaN stays in the smoothing state and
            // every pose after it is NaN too, whatever the tracker sends.
            // Holding the last pose and restarting from the next sample clears it.
            if (!pose.IsFinite)
            {
                RejectedPoses++;
                ResetInterpolators();
                ResetSmoothing();
            }
            else
            {
                _lastAppliedPose = pose.Scaled(scale);
            }

            _wasApplying = true;
            IsApplying = true;
            return _lastAppliedPose;
        }

        if (_isTransitioningOut)
        {
            AdvanceTransitionOut(unscaledDeltaTime);
        }
        else if (_wasApplying)
        {
            _isTransitioningOut = true;
            _transitionOutProgress = 0f;
            AdvanceTransitionOut(unscaledDeltaTime);
        }

        if (_isTransitioningOut)
        {
            IsApplying = true;
            return HeadPose.Lerp(_lastAppliedPose, HeadPose.Zero, _transitionOutProgress);
        }

        IsApplying = false;
        return HeadPose.Zero;
    }

    /// <summary>
    /// Drops everything with no fade, for a camera cut - a level load or the game
    /// replacing the player rig - where easing out of the previous room's pose would
    /// show as a drift.
    /// </summary>
    internal void ResetState()
    {
        _isTransitioningIn = false;
        _isTransitioningOut = false;
        _transitionInProgress = 0f;
        _transitionOutProgress = 0f;
        _wasApplying = false;
        IsApplying = false;
        _detected6Dof = false;
        _lastAppliedPose = HeadPose.Zero;
        ResetSmoothing();
        ResetInterpolators();
    }

    /// <summary>Drops the position smoothing and interpolation, for the mode cycle.</summary>
    internal void ResetPosition()
    {
        _positionProcessor.Reset();
        _positionInterpolator.Reset();
        _detected6Dof = false;
    }

    private Vec3 ComputePosition(float deltaTime)
    {
        if (!PositionEnabled)
        {
            _detected6Dof = false;
            return Vec3.Zero;
        }

        PositionData rawPosition = _receiver.GetLatestPosition();
        if (!_detected6Dof && (rawPosition.X != 0f || rawPosition.Y != 0f || rawPosition.Z != 0f))
        {
            _detected6Dof = true;
        }
        if (!_detected6Dof) return Vec3.Zero;

        PositionData interpolated = _positionInterpolator.Update(rawPosition, deltaTime);

        // The processor subtracts the arc a face point traces about the neck, which is
        // a property of the physical head, so it takes the tracker-convention rotation.
        _processor.GetSmoothedRotation(out float yaw, out float pitch, out float roll);
        Quat4 physicalRotation = QuaternionUtils.FromYawPitchRoll(yaw, pitch, roll);

        // Box-clamped by the processor against [-LimitX, LimitX], [-LimitYDown, LimitY]
        // and [-LimitZ, LimitZBack].
        return _positionProcessor.Process(interpolated, physicalRotation, deltaTime);
    }

    private void BeginSession()
    {
        _isTransitioningIn = true;
        _transitionInProgress = 0f;
        _detected6Dof = false;
        _lastAppliedPose = HeadPose.Zero;
        ResetInterpolators();
        ResetSmoothing();
    }

    /// <summary>
    /// Tracking came back before the fade out finished - a gate that closed and
    /// reopened inside it, or the toggle pressed twice. The fade in starts from the
    /// fraction of the pose still on screen, so the view neither jumps back to the
    /// full pose nor drops to zero first.
    /// </summary>
    private void ResumeFromFadeOut()
    {
        float inScale = _isTransitioningIn ? _transitionInProgress * _transitionInProgress : 1f;
        float onScreen = (1f - _transitionOutProgress) * inScale;
        _isTransitioningOut = false;
        _isTransitioningIn = true;
        _transitionInProgress = MathF.Sqrt(onScreen);
    }

    private float AdvanceTransitionIn(float deltaTime)
    {
        if (!_isTransitioningIn) return 1f;

        // Scaled time, matching the interpolator and processor alongside it.
        _transitionInProgress += deltaTime / TransitionInDuration;
        if (_transitionInProgress >= 1f)
        {
            _transitionInProgress = 1f;
            _isTransitioningIn = false;
        }
        return _transitionInProgress * _transitionInProgress;
    }

    private void AdvanceTransitionOut(float unscaledDeltaTime)
    {
        // Unscaled: the pause menu zeroes timeScale, and on scaled time the fade could
        // never complete, leaving the menu rendered through a turned view.
        _transitionOutProgress += unscaledDeltaTime / TransitionOutDuration;
        if (_transitionOutProgress >= 1f)
        {
            _transitionOutProgress = 1f;
            _isTransitioningOut = false;
            _wasApplying = false;
        }
    }

    private void ResetInterpolators()
    {
        _interpolator.Reset();
        _positionInterpolator.Reset();
    }

    private void ResetSmoothing()
    {
        _processor.ResetSmoothing();
        _positionProcessor.ResetSmoothing();
    }
}
