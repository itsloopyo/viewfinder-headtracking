// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System.Text;
using UnityEngine;
using ViewfinderHeadTracking.Camera;
using ViewfinderHeadTracking.Core;
using ViewfinderHeadTracking.Tracking;
using ViewfinderHeadTracking.Utilities;

namespace ViewfinderHeadTracking.Diagnostics;

/// <summary>
/// The diagnostic lines that answer a "head tracking is wrong" report from the log
/// alone. Every term a reticle or axis fault can live in lands on ONE line for ONE
/// frame: the pose the camera was written with, the lean, the aim distance, the
/// screen offset and the collision allowance.
///
/// Rate limited by time, never capped by count, so a probe answers the tenth
/// question put to it as well as the first.
/// </summary>
internal static class RigProbe
{
    private const float SampleIntervalSeconds = 0.5f;

    internal static bool Enabled;

    private static float _nextSample;
    private static int _verifiedCamera;

    /// <summary>
    /// Checks the mod's projection against the engine's own on the CLEAN view, once per
    /// camera. Both sides then describe the same camera, so a disagreement is
    /// arithmetic, and agreement rules out handedness and clip-convention faults
    /// before any reticle maths is trusted.
    /// </summary>
    internal static void VerifyProjection(UnityEngine.Camera camera, Vector3 cleanPosition, Quaternion cleanRotation)
    {
        if (!Enabled) return;
        int id = camera.GetInstanceID();
        if (_verifiedCamera == id) return;
        _verifiedCamera = id;

        var check = new TrackedView();
        check.Build(camera, cleanPosition, cleanRotation, cleanPosition, cleanRotation, Time.frameCount);

        Vector3 forward = cleanRotation * Vector3.forward;
        Vector3 right = cleanRotation * Vector3.right;
        Vector3 up = cleanRotation * Vector3.up;

        var sb = new StringBuilder("PROJCHECK ");
        foreach (Vector3 probe in new[] { cleanPosition + forward * 5f, cleanPosition + forward * 5f + right * 1.5f, cleanPosition + forward * 5f + up * 1.5f })
        {
            check.TryProjectPoint(probe, out Vector2 ours);
            Vector3 engine = camera.WorldToScreenPoint(probe);
            sb.Append($"[ours=({ours.x:F1},{ours.y:F1}) engine=({engine.x:F1},{engine.y:F1})] ");
        }
        HeadTrackingPlugin.Logger.LogInfo(sb.ToString());
        HeadTrackingPlugin.Logger.LogInfo(
            $"RIG camera={TransformPath.GetFullPath(camera.transform)} fov={camera.fieldOfView:F2} " +
            $"near={camera.nearClipPlane:F3} far={camera.farClipPlane:F1} aspect={camera.aspect:F4} " +
            $"pixels={camera.pixelWidth}x{camera.pixelHeight} mask=0x{camera.cullingMask:X8}");
    }

    internal static void Sample(TrackedView view, HeadPose applied, float aimScale, float zoom,
        float aimDistance, RaycastHit aimHit, Vector2 reticleOffset, bool reticleMoved,
        Vector3 wantedLean, LeanClamp clamp, bool collisionEnabled)
    {
        if (!Enabled || Time.unscaledTime < _nextSample) return;
        _nextSample = Time.unscaledTime + SampleIntervalSeconds;

        Vector3 cleanRight = view.CleanRotation * Vector3.right;
        Vector3 cleanUp = view.CleanRotation * Vector3.up;
        Vector3 cleanForward = view.CleanRotation * Vector3.forward;
        Vector3 trackedForward = view.RenderRotation * Vector3.forward;
        Vector3 trackedUp = view.RenderRotation * Vector3.up;
        Vector3 lean = view.RenderPosition - view.CleanPosition;
        int aimLayer = aimDistance >= 0f ? aimHit.collider.gameObject.layer : -1;

        HeadTrackingPlugin.Logger.LogInfo(
            $"AIMGEO pose=(y{applied.Yaw:F2},p{applied.Pitch:F2},r{applied.Roll:F2}," +
            $"x{applied.Position.X:F3},y{applied.Position.Y:F3},z{applied.Position.Z:F3}) " +
            $"aimScale={aimScale:F2} zoom={zoom:F4} " +
            $"turnR={Vector3.Dot(trackedForward, cleanRight):F4} turnU={Vector3.Dot(trackedForward, cleanUp):F4} " +
            $"tiltR={Vector3.Dot(trackedUp, cleanRight):F4} " +
            $"leanR={Vector3.Dot(lean, cleanRight):F3} leanU={Vector3.Dot(lean, cleanUp):F3} leanF={Vector3.Dot(lean, cleanForward):F3} " +
            $"wanted={wantedLean.magnitude:F3} dist={aimDistance:F3} layer={aimLayer} " +
            $"offset=({reticleOffset.x:F1},{reticleOffset.y:F1})px moved={reticleMoved} " +
            $"clamp(enabled={collisionEnabled} contact={clamp.InContact} allow={clamp.Allowance:F3}) " +
            $"screen={view.PixelWidth}x{view.PixelHeight}");
    }
}
