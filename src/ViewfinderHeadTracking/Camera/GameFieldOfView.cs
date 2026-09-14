// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;
using ViewfinderHeadTracking.Core;
using ViewfinderHeadTracking.Game;

namespace ViewfinderHeadTracking.Camera;

/// <summary>
/// The two fields of view the zoom correction is built from, read live every frame.
///
/// The live one comes off the projection matrix, whose m11 is
/// <c>1 / tan(fovVertical / 2)</c> by construction, so it is a vertical half-angle
/// tangent whatever wrote the projection.
///
/// The base is <c>PlayerCameraController.fovSetting</c>, which reads the same as the
/// world camera's <c>fieldOfView</c> - a vertical angle in degrees - in ordinary
/// play. The player's Graphics FOV slider is a HORIZONTAL angle; the game converts
/// it to this vertical one at a fixed 16:9 (slider 90 gives 58.7155, 100 gives
/// 67.6728) whatever the window's aspect, and a window resize changes neither. Both
/// terms are vertical, which is what makes the logged factor read 1.0000 there.
///
/// The basis is logged again whenever the base moves, so a change in the game's
/// settings shows up with its factor rather than leaving the first line standing.
/// </summary>
internal sealed class GameFieldOfView
{
    private int _loggedBasisForCamera;
    private float _loggedBase = -1f;
    private bool _loggedUnreadable;

    /// <summary>What yaw, pitch and the lean are scaled by this frame. 1.0 when nothing zooms.</summary>
    internal float Factor { get; private set; } = 1f;

    internal void Update(UnityEngine.Camera camera, PlayerRig rig, bool inGameplay)
    {
        float m11 = camera.projectionMatrix.m11;
        float tanHalfLive = 1f / m11;
        float liveVerticalFov = Mathf.Atan(tanHalfLive) * 2f * Mathf.Rad2Deg;

        float baseVerticalFov = rig.FovSetting;
        float tanHalfBase = Mathf.Tan(baseVerticalFov * 0.5f * Mathf.Deg2Rad);

        if (!ZoomCompensation.IsUsableTangent(tanHalfLive) || !ZoomCompensation.IsUsableTangent(tanHalfBase))
        {
            Factor = 1f;
            if (inGameplay && !_loggedUnreadable)
            {
                _loggedUnreadable = true;
                // So the basis is logged again once it reads, rather than this warning
                // standing as the last word while compensation is back on.
                _loggedBasisForCamera = 0;
                HeadTrackingPlugin.Logger.LogWarning(
                    $"FOVBASIS unreadable, zoom compensation off: projectionMatrix.m11={m11:F5} " +
                    $"PlayerCameraController.fovSetting={baseVerticalFov:F4}");
            }
            return;
        }

        _loggedUnreadable = false;
        Factor = ZoomCompensation.Factor(tanHalfLive, tanHalfBase);

        if (!inGameplay) return;

        int cameraId = camera.GetInstanceID();
        if (_loggedBasisForCamera == cameraId && _loggedBase == baseVerticalFov) return;
        _loggedBasisForCamera = cameraId;
        _loggedBase = baseVerticalFov;

        HeadTrackingPlugin.Logger.LogInfo(
            $"FOVBASIS live={liveVerticalFov:F4} deg vertical (projectionMatrix.m11={m11:F5}, tan(half)={tanHalfLive:F5}) " +
            $"base={baseVerticalFov:F4} deg vertical (PlayerCameraController.fovSetting, tan(half)={tanHalfBase:F5}) " +
            $"camera.fieldOfView={camera.fieldOfView:F4} aspect={camera.aspect:F4} factor={Factor:F4}");
    }
}
