// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Reflection;
using UnityEngine;
using ViewfinderHeadTracking.Core;
using ViewfinderHeadTracking.Utilities;

namespace ViewfinderHeadTracking.Camera;

/// <summary>
/// Moves Viewfinder's own reticle - the dot <c>ReticleController.reticleImage</c> on
/// <c>Game Controller/Canvas/Reticle</c> - onto the point the player is actually
/// aiming at, and puts it back when tracking stops.
///
/// The move is made from <c>Canvas.willRenderCanvases</c>, after every game script's
/// LateUpdate and before the canvas is batched, so the reticle drawn this frame is
/// the one computed from this frame's view. The position the game had it at is
/// captured on the first move and put back whenever tracking stops.
///
/// The screen point is turned into the canvas's own space with
/// <c>RectTransformUtility</c> against the reticle's parent rect, which takes care
/// of the canvas scaler and render mode instead of assuming either.
/// </summary>
internal sealed class GameReticle
{
    private const int SearchRetryFrames = 30;

    private MethodInfo _reticleImageGetter = null!;

    private RectTransform? _rect;
    private RectTransform? _parent;
    private Canvas? _canvas;
    private Vector3 _restPosition;
    private bool _moved;
    private int _framesUntilSearch;

    /// <summary>Whether the reticle is currently away from where the game had it.</summary>
    internal bool Moved => _moved;

    internal void ResolveMembers()
    {
        _reticleImageGetter = GameMembers.RequireGetter(GameTypes.ReticleController, "reticleImage");
    }

    /// <summary>Places the reticle at a screen point, in pixels from the bottom left.</summary>
    internal void MoveTo(Vector2 screenPoint, Vector2 screenCentre)
    {
        if (!Resolve()) return;

        UnityEngine.Camera? eventCamera = _canvas!.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screenPoint, eventCamera, out Vector2 target)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screenCentre, eventCamera, out Vector2 centre))
        {
            Restore();
            return;
        }

        if (!_moved)
        {
            _restPosition = _rect!.localPosition;
            _moved = true;
        }

        Vector2 delta = target - centre;
        _rect!.localPosition = new Vector3(_restPosition.x + delta.x, _restPosition.y + delta.y, _restPosition.z);
    }

    /// <summary>Puts the reticle back where the game had it.</summary>
    internal void Restore()
    {
        if (!_moved) return;
        _moved = false;
        if (_rect != null) _rect.localPosition = _restPosition;
    }

    private bool Resolve()
    {
        if (_rect != null) return true;

        // A destroyed reticle compares null under Unity's ==; drop what was held.
        _moved = false;
        _parent = null;
        _canvas = null;

        if (--_framesUntilSearch > 0) return false;
        _framesUntilSearch = SearchRetryFrames;

        object? controller = GameMembers.FindObjectOfType(GameTypes.ReticleController);
        if (controller == null) return false;

        var image = (Component?)_reticleImageGetter.Invoke(controller, null);
        if (image == null) return false;

        // Il2CppInterop types both as Transform, the declared type, so they are
        // re-wrapped around the same native object to reach the RectTransform members.
        var rect = (RectTransform)GameMembers.Rewrap(image.transform, typeof(RectTransform));
        _parent = (RectTransform)GameMembers.Rewrap(rect.parent, typeof(RectTransform));
        _canvas = RootCanvasOf(rect);
        _rect = rect;

        HeadTrackingPlugin.Logger.LogInfo(
            $"Reticle: {TransformPath.GetFullPath(rect)} on canvas '{_canvas.name}' " +
            $"({_canvas.renderMode}, scale {_canvas.scaleFactor:F3}), rest position {rect.localPosition}");
        return true;
    }

    /// <summary>The root canvas that draws the reticle.</summary>
    private static Canvas RootCanvasOf(Transform node)
    {
        for (Transform? current = node; current != null; current = current.parent)
        {
            Canvas canvas = current.GetComponent<Canvas>();
            if (canvas != null) return canvas.rootCanvas;
        }

        throw new InvalidOperationException($"No Canvas above {TransformPath.GetFullPath(node)}");
    }
}
