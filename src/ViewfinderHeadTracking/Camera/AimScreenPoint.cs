// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace ViewfinderHeadTracking.Camera;

/// <summary>
/// Where on the tracked view the reticle belongs: the screen point of whatever the
/// clean aim ray lands on.
/// </summary>
internal static class AimScreenPoint
{
    private const float EdgeMarginPixels = 24f;

    // An aim point behind the eye whose sideways offset is within this fraction of its
    // depth counts as straight behind. Pinning it to an edge instead lets a few
    // micrometres of tracker noise pick the edge, and the reticle then jumps between
    // opposite edges from frame to frame.
    private const float StraightBehindTangent = 0.1f;

    /// <summary>
    /// The impact POINT when the ray hits, so a lean moves the reticle by exactly the
    /// parallax that surface has, and the aim DIRECTION on a definite no-hit, which is
    /// a target at infinity. Always inside the screen by the edge margin.
    /// </summary>
    internal static Vector2 Locate(TrackedView view, Vector3 origin, Vector3 direction, int mask,
        out float distance, out RaycastHit hit)
    {
        bool isHit = AimTrace.Trace(origin, direction, mask, out distance, out hit);
        Vector3 aimPoint = origin + direction * distance;
        bool onScreen = isHit
            ? view.TryProjectPoint(aimPoint, out Vector2 screenPoint)
            : view.TryProjectDirection(direction, out screenPoint);

        if (!onScreen)
        {
            // Measured from the RENDERED eye: a lean can carry the eye past a near
            // surface, which puts the point behind it whichever way the clean aim faces.
            Vector3 fromEye = isHit ? aimPoint - view.RenderPosition : direction;
            Vector3 viewDirection = Quaternion.Inverse(view.RenderRotation) * fromEye;
            screenPoint = EdgePoint(viewDirection, view.PixelWidth, view.PixelHeight);
        }

        return ClampToScreen(screenPoint, view.PixelWidth, view.PixelHeight);
    }

    /// <summary>
    /// Pins the reticle to the screen edge in the direction of an aim point that has
    /// fallen behind the rendered eye, or to the centre when it is straight behind.
    /// </summary>
    internal static Vector2 EdgePoint(Vector3 viewDirection, float pixelWidth, float pixelHeight)
    {
        float dominant = Mathf.Max(Mathf.Abs(viewDirection.x), Mathf.Abs(viewDirection.y));
        if (dominant <= Mathf.Abs(viewDirection.z) * StraightBehindTangent)
        {
            return new Vector2(pixelWidth * 0.5f, pixelHeight * 0.5f);
        }
        return new Vector2(
            pixelWidth * (0.5f + viewDirection.x / dominant),
            pixelHeight * (0.5f + viewDirection.y / dominant));
    }

    internal static Vector2 ClampToScreen(Vector2 screenPoint, float pixelWidth, float pixelHeight)
    {
        return new Vector2(
            Mathf.Clamp(screenPoint.x, EdgeMarginPixels, pixelWidth - EdgeMarginPixels),
            Mathf.Clamp(screenPoint.y, EdgeMarginPixels, pixelHeight - EdgeMarginPixels));
    }
}
