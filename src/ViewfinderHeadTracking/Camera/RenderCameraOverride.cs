// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System.Collections.Generic;
using UnityEngine;

namespace ViewfinderHeadTracking.Camera;

/// <summary>
/// Puts the tracked view on a player world camera for the length of one render and
/// takes it back off afterwards, so outside the render the camera's matrix is always
/// the one its transform gives.
/// </summary>
internal sealed class RenderCameraOverride
{
    private readonly List<UnityEngine.Camera> _applied = new();

    /// <summary>
    /// Writes the tracked view onto <paramref name="camera"/> if it is one of the
    /// player's world cameras; any other camera is left alone.
    /// </summary>
    internal void Apply(UnityEngine.Camera camera, IReadOnlyList<UnityEngine.Camera> worldCameras,
        Matrix4x4 inverseHeadDelta)
    {
        // Indexed rather than foreach: this runs inside every camera render, and
        // enumerating an interface boxes its enumerator.
        for (int i = 0; i < worldCameras.Count; i++)
        {
            if (worldCameras[i] != camera) continue;

            // Reset first so the clean matrix is derived from the transform again,
            // never from last render's tracked one.
            camera.ResetWorldToCameraMatrix();
            camera.worldToCameraMatrix = camera.worldToCameraMatrix * inverseHeadDelta;
            _applied.Add(camera);
            return;
        }
    }

    /// <summary>Hands <paramref name="camera"/> back to the game once it has rendered.</summary>
    internal void EndRender(UnityEngine.Camera camera)
    {
        for (int i = _applied.Count - 1; i >= 0; i--)
        {
            UnityEngine.Camera applied = _applied[i];
            if (applied != camera) continue;
            _applied.RemoveAt(i);
            if (applied != null) applied.ResetWorldToCameraMatrix();
        }
    }

    /// <summary>Hands every camera still carrying the tracked view back to the game.</summary>
    internal void ReleaseAll()
    {
        foreach (UnityEngine.Camera applied in _applied)
        {
            if (applied != null) applied.ResetWorldToCameraMatrix();
        }
        _applied.Clear();
    }
}
