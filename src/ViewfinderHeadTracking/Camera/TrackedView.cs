// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace ViewfinderHeadTracking.Camera;

/// <summary>
/// The head-tracked view the current frame is drawn with.
///
/// Everything the reticle needs is derived from the view that is actually written
/// onto the camera, never re-derived from the tracker's yaw/pitch/roll: one
/// derivation used twice cannot disagree with itself.
/// </summary>
internal sealed class TrackedView
{
    // Points at or behind the rendered view's plane have no meaningful screen
    // position, and a reticle at 1e30 is a NaN on its way to a vertex buffer.
    private const float MinClipW = 1e-4f;

    // The frame this view was built on; the render hook applies nothing older.
    private int _frame = -1;
    private Matrix4x4 _viewProjection;

    internal Quaternion CleanRotation { get; private set; }
    internal Vector3 CleanPosition { get; private set; }
    internal Quaternion RenderRotation { get; private set; }
    internal Vector3 RenderPosition { get; private set; }

    /// <summary>
    /// World-space transform that takes the clean camera to the tracked one, inverted:
    /// a camera's own clean <c>worldToCameraMatrix</c> times this is its tracked view.
    /// Applying the same delta to every player world camera keeps them aligned whatever
    /// offsets they sit at from one another.
    /// </summary>
    internal Matrix4x4 InverseHeadDelta { get; private set; } = Matrix4x4.identity;

    internal float PixelWidth { get; private set; }
    internal float PixelHeight { get; private set; }

    /// <summary>Whether this view was built on <paramref name="frame"/> and not invalidated since.</summary>
    internal bool IsCurrent(int frame) => _frame == frame;

    /// <summary>Marks the view stale, so the render hook leaves the camera as the game set it.</summary>
    internal void Invalidate() => _frame = -1;

    internal void Build(UnityEngine.Camera camera, Vector3 cleanPosition, Quaternion cleanRotation,
        Vector3 renderPosition, Quaternion renderRotation, int frame)
    {
        CleanPosition = cleanPosition;
        CleanRotation = cleanRotation;
        RenderPosition = renderPosition;
        RenderRotation = renderRotation;

        // The tracked pose is a rigid transform, so its inverse is written down
        // directly rather than solved for, and the view matrix is that same inverse.
        Matrix4x4 trackedInverse = RigidInverse(renderRotation, renderPosition);
        InverseHeadDelta = Matrix4x4.TRS(cleanPosition, cleanRotation, Vector3.one) * trackedInverse;

        // The projection matrix, not fieldOfView plus aspect, is the field of view the
        // frame is drawn with.
        _viewProjection = camera.projectionMatrix * ToViewMatrix(trackedInverse);
        PixelWidth = camera.pixelWidth;
        PixelHeight = camera.pixelHeight;
        _frame = frame;
    }

    /// <summary>
    /// A Unity view matrix from a world rotation and position. Unity's
    /// <c>worldToCameraMatrix</c> looks down -Z, so the third row is negated.
    /// </summary>
    internal static Matrix4x4 BuildViewMatrix(Quaternion rotation, Vector3 position)
    {
        return ToViewMatrix(RigidInverse(rotation, position));
    }

    private static Matrix4x4 RigidInverse(Quaternion rotation, Vector3 position)
    {
        return Matrix4x4.Rotate(Quaternion.Inverse(rotation)) * Matrix4x4.Translate(-position);
    }

    private static Matrix4x4 ToViewMatrix(Matrix4x4 m)
    {
        m.m20 = -m.m20;
        m.m21 = -m.m21;
        m.m22 = -m.m22;
        m.m23 = -m.m23;
        return m;
    }

    /// <summary>Projects a world point through the tracked view, in screen pixels.</summary>
    internal bool TryProjectPoint(Vector3 worldPoint, out Vector2 screenPoint)
    {
        return Project(new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1f), out screenPoint);
    }

    /// <summary>
    /// Projects a world DIRECTION through the tracked view - a target at infinity,
    /// which is what a definite no-hit down the aim ray is.
    /// </summary>
    internal bool TryProjectDirection(Vector3 worldDirection, out Vector2 screenPoint)
    {
        return Project(new Vector4(worldDirection.x, worldDirection.y, worldDirection.z, 0f), out screenPoint);
    }

    private bool Project(Vector4 homogeneous, out Vector2 screenPoint)
    {
        Vector4 clip = _viewProjection * homogeneous;

        // Unity's managed projectionMatrix is OpenGL convention whatever the graphics
        // API, so clip.w is -viewSpaceZ and positive in front of the camera. PROJCHECK
        // in the diagnostics compares this against Camera.WorldToScreenPoint.
        if (clip.w <= MinClipW)
        {
            screenPoint = default;
            return false;
        }

        float invW = 1f / clip.w;
        screenPoint = new Vector2(
            (clip.x * invW * 0.5f + 0.5f) * PixelWidth,
            (clip.y * invW * 0.5f + 0.5f) * PixelHeight);
        return true;
    }
}
