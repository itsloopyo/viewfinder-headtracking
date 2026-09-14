// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using CameraUnlock.Core.Data;
using UnityEngine;
using ViewfinderHeadTracking.Camera;

namespace ViewfinderHeadTracking.Tracking;

/// <summary>
/// The head offset for one frame in the tracker's own convention, and the single
/// place it is turned into a camera pose. The render write and the reticle
/// projection both consume what <see cref="ComposeRotation"/> and <see cref="LeanWorld"/> return, so there is one
/// derivation and nothing that can disagree with itself on a combined pose.
/// </summary>
internal struct HeadPose
{
    // Below this the clean view has no heading left in its forward axis, which
    // happens only when the player looks straight up or straight down.
    private const float MinFlatForwardSqrMagnitude = 1e-6f;

    // Applied after the pipeline so an extreme pose cannot swing the view somewhere the
    // player cannot recover from. Yaw is held inside the tangent pole by ZoomCompensation.
    private const float MaxPitch = 70f;
    private const float MaxRoll = 45f;

    internal float Yaw;
    internal float Pitch;
    internal float Roll;

    /// <summary>The lean in metres, after the processor's clamp, tracker axes.</summary>
    internal Vec3 Position;

    internal static HeadPose Zero => new() { Position = Vec3.Zero };

    internal bool IsFinite =>
        float.IsFinite(Yaw) && float.IsFinite(Pitch) && float.IsFinite(Roll)
        && float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z);

    internal static HeadPose Lerp(HeadPose a, HeadPose b, float t)
    {
        return new HeadPose
        {
            Yaw = Mathf.Lerp(a.Yaw, b.Yaw, t),
            Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t),
            Roll = Mathf.Lerp(a.Roll, b.Roll, t),
            Position = Vec3.Lerp(a.Position, b.Position, t)
        };
    }

    internal HeadPose Scaled(float scale)
    {
        return new HeadPose
        {
            Yaw = Yaw * scale,
            Pitch = Pitch * scale,
            Roll = Roll * scale,
            Position = Position * scale
        };
    }

    /// <summary>
    /// The zoom correction and the safety limits, applied to the tracker-convention
    /// pose before it is composed. Yaw and pitch move the picture across the frame,
    /// so both are scaled by how much the current field of view magnifies it against
    /// the player's own setting; the lean likewise, linearly. Roll spins the picture
    /// by the same angle at every field of view, so it is left alone.
    /// </summary>
    internal HeadPose WithZoomAndLimits(float zoom)
    {
        return new HeadPose
        {
            Yaw = ZoomCompensation.ScaleAngle(Yaw, zoom),
            Pitch = Mathf.Clamp(ZoomCompensation.ScaleAngle(Pitch, zoom), -MaxPitch, MaxPitch),
            Roll = Mathf.Clamp(Roll, -MaxRoll, MaxRoll),
            Position = Position * zoom
        };
    }

    /// <summary>
    /// The engine boundary for rotation. Unity's positive rotation about x pitches
    /// the nose DOWN, so pitch is mirrored; yaw and roll pass through. These are the
    /// signs superliminal-headtracking arrived at from a player's report on a real
    /// tracker with this exact composition; a scripted sweep in Viewfinder measured the
    /// same six relationships. Never a user setting.
    /// </summary>
    internal float EngineYaw => Yaw;
    internal float EnginePitch => -Pitch;
    internal float EngineRoll => Roll;

    /// <summary>
    /// The engine boundary for translation, in a camera-shaped basis: +x right, +y
    /// up, +z forward. x arrives mirrored from the tracker, and the processor calls
    /// NEGATIVE z the forward lean while Unity's +z is forward, so both flip. They
    /// flip here, after the processor's asymmetric clamp, because flipping through
    /// PositionSettings.InvertZ would hand the forward lean the tight backward budget.
    /// </summary>
    internal Vector3 EngineOffset => new(-Position.X, Position.Y, -Position.Z);

    /// <summary>
    /// The tracked rotation. World-space yaw turns the view about world up, so the
    /// horizon stays level when the player is looking up or down; camera-local yaw
    /// turns it about the view's own up.
    /// </summary>
    internal Quaternion ComposeRotation(Quaternion cleanRotation, bool worldSpaceYaw)
    {
        return worldSpaceYaw
            ? Quaternion.AngleAxis(EngineYaw, Vector3.up) * cleanRotation * Quaternion.Euler(EnginePitch, 0f, EngineRoll)
            : cleanRotation * Quaternion.Euler(EnginePitch, EngineYaw, EngineRoll);
    }

    /// <summary>
    /// The lean as a world-space vector from the clean eye, in a horizon-locked basis
    /// taken from the clean view's heading, so leaning forward while looking at the
    /// floor moves the eye forward over the floor rather than down into it.
    /// </summary>
    internal Vector3 LeanWorld(Quaternion cleanRotation)
    {
        Vector3 offset = EngineOffset;
        if (offset == Vector3.zero) return Vector3.zero;

        Vector3 viewForward = cleanRotation * Vector3.forward;
        Vector3 flatForward = Vector3.ProjectOnPlane(viewForward, Vector3.up);
        if (flatForward.sqrMagnitude < MinFlatForwardSqrMagnitude)
        {
            // Straight up or down: the heading lives in the view's up axis, which
            // points along it when looking down and against it when looking up.
            flatForward = Vector3.ProjectOnPlane(cleanRotation * Vector3.up, Vector3.up)
                          * -Mathf.Sign(Vector3.Dot(viewForward, Vector3.up));
        }
        flatForward.Normalize();
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);

        return flatRight * offset.x + Vector3.up * offset.y + flatForward * offset.z;
    }
}
