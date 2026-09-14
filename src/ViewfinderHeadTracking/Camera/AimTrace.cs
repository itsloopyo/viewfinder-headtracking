// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace ViewfinderHeadTracking.Camera;

/// <summary>
/// Finds the surface the player is pointing at along the CLEAN camera's forward
/// axis, so the reticle is drawn on the point rather than on the direction.
///
/// The layers are the game's own <c>Interactor.layerMask</c>, read off the rig
/// (Default only, on every rig seen). Triggers are ignored. Nothing here filters on
/// object names.
///
/// The cast runs on the frame whose view it feeds, with no smoothing, rate limit or
/// carried-over depth, because a reticle glued to a surface has to jump when the
/// aim crosses an edge.
/// </summary>
internal static class AimTrace
{
    // The player camera's far clip is 1000; nothing beyond it is drawn, and
    // the parallax a lean produces at that range is far under a pixel.
    private const float MaxDistance = 1000f;

    /// <summary>
    /// Returns true with the distance along the aim direction when a surface is hit,
    /// false for a definite no-hit, which the caller treats as a target at infinity.
    /// The hit is handed back for diagnostics, which read its collider only on a frame
    /// they log: every read of it creates interop wrapper objects.
    /// </summary>
    internal static bool Trace(Vector3 origin, Vector3 direction, int mask, out float distance, out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, direction, out hit, MaxDistance, mask, QueryTriggerInteraction.Ignore))
        {
            distance = -1f;
            return false;
        }

        // The contact's own position projected onto the aim direction is a distance by
        // construction, rather than a field taken on trust.
        distance = Vector3.Dot(hit.point - origin, direction);
        return true;
    }
}
