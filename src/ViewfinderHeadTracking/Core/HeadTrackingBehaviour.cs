// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections.Generic;
using CameraUnlock.Core.Ads;
using CameraUnlock.Core.Protocol;
using UnityEngine;
using UnityEngine.Rendering;
using ViewfinderHeadTracking.Camera;
using ViewfinderHeadTracking.Configuration;
using ViewfinderHeadTracking.Diagnostics;
using ViewfinderHeadTracking.Game;
using ViewfinderHeadTracking.Input;
using ViewfinderHeadTracking.Tracking;
using ViewfinderHeadTracking.Utilities;

namespace ViewfinderHeadTracking.Core;

/// <summary>
/// Drives head tracking for Viewfinder.
///
/// Viewfinder renders through URP, which calls no OnPreCull or OnPostRender, so the
/// view is put on the camera from <c>RenderPipelineManager.beginCameraRendering</c>
/// and taken back off in <c>endCameraRendering</c>. A matrix written there is the one
/// the frame is culled and drawn with: a tracked 45 degree turn renders the same
/// picture as the game's own camera turned 45 degrees, props at the edges included.
///
/// Neither the camera's transform nor its matrix outside the render is ever touched.
/// That is the whole of aim decoupling here: <c>Interactor</c> casts its ray from
/// <c>playerViewTransform</c>, and every script that runs during Update and
/// LateUpdate finds the camera exactly where the mouse put it - its
/// <c>worldToCameraMatrix</c> read in LateUpdate matches its transform with a pose
/// applied, and the interaction target does not change as the head turns.
///
/// The frame itself is computed in <c>Canvas.willRenderCanvases</c>, which fires
/// after every script's LateUpdate and before the canvases are batched and the
/// cameras render. That is late enough to read the camera where the game left it for
/// the frame and early enough for the reticle moved there to be drawn this frame.
/// </summary>
public class HeadTrackingBehaviour : MonoBehaviour
{
    // The game fills playerWorldCameras when the rig is built and never has been seen
    // to change it, so it is re-read twice a second rather than on every frame.
    private const int WorldCameraRefreshFrames = 30;

    // A sender that keeps overflowing would otherwise write a line every frame.
    private const float RejectedPoseLogIntervalSeconds = 10f;

    // A level is still settling when its player camera first appears, so tracking
    // waits this long after the rig is found before it starts to fade in.
    private const float RigWarmupSeconds = 1.5f;

    // Further than the clean camera moves in one frame of play, so the game has moved
    // the player and the last spot's collision allowance says nothing about this one.
    // Tightening is instant either way, so mistaking a fast frame for a move can only
    // cut short a release ease in progress, opening the lean at once.
    private const float TeleportMetres = 1f;

    private OpenTrackReceiver _receiver = null!;
    private TrackingPipeline _pipeline = null!;
    private HotkeyHandler _hotkeys = null!;
    private LeanClamp _leanClamp = null!;

    private readonly PlayerRig _rig = new();
    private readonly GameReticle _reticle = new();
    private readonly GameFieldOfView _fieldOfView = new();
    private readonly TrackedView _view = new();
    private readonly AdsFade _aimFade = new();
    private readonly WindowPlacement _windowPlacement = new();
    private readonly RenderCameraOverride _renderOverride = new();
    private readonly TrackerConnectionMonitor _trackerConnection = new();
    private readonly List<Il2CppEvent> _events = new();
    private GameplayState _state = null!;

    // Held as fields so the per-frame Guard calls do not allocate a delegate each time.
    private Action _pollGame = null!;
    private Action _placeWindow = null!;
    private Action _computeFrame = null!;
    private Action _applyToRenderingCamera = null!;
    private UnityEngine.Camera? _renderingCamera;

    private bool _trackingEnabled;
    private bool _worldSpaceYaw;
    private bool _showReticle;
    private bool _pauseOnLostFocus;
    private bool _collisionEnabled;

    private bool _initialized;
    private bool _faulted;
    private bool _loggedFirstPose;

    private int _lastFrame = -1;
    private int _framesUntilWorldCameraRefresh;
    private HeadPose _pose = HeadPose.Zero;
    private float _aimScale = 1f;
    private int _loggedRejectedPoses;
    private float _nextRejectedPoseLog;
    private float _rigReadyTime;
    private Vector3 _lastCleanPosition;
    private bool _hasLastCleanPosition;

    internal void Initialize(OpenTrackReceiver receiver, TrackingPipeline pipeline, PluginConfig config)
    {
        _pollGame = PollGame;
        _placeWindow = _windowPlacement.Update;
        _computeFrame = ComputeFrame;
        _applyToRenderingCamera = ApplyToRenderingCamera;

        _receiver = receiver;
        _pipeline = pipeline;

        _trackingEnabled = config.EnabledOnStartup.Value;
        _worldSpaceYaw = config.WorldSpaceYaw.Value;
        _showReticle = config.ShowReticle.Value;
        _pauseOnLostFocus = config.PauseOnLostFocus.Value;
        _collisionEnabled = config.CollisionEnabled.Value;
        _pipeline.PositionEnabled = config.PositionEnabled.Value;

        _leanClamp = new LeanClamp(config.CollisionRadius.Value, config.CollisionReleaseSmoothing.Value);
        _hotkeys = new HotkeyHandler(config, this);
        _hotkeys.LogBindings();

        _rig.ResolveMembers();
        _reticle.ResolveMembers();
        _state = new GameplayState(_rig) { DiagnosticLogging = config.DiagnosticLogging.Value };
        _state.ResolveMembers();
        RigProbe.Enabled = config.DiagnosticLogging.Value;

        _rig.OnRigChanged += OnRigChanged;

        _events.Add(Il2CppEvent.Subscribe(typeof(Canvas), "willRenderCanvases", new Action(OnWillRenderCanvases)));
        _events.Add(Il2CppEvent.Subscribe(typeof(RenderPipelineManager), "beginCameraRendering",
            new Action<ScriptableRenderContext, UnityEngine.Camera>(OnBeginCameraRendering)));
        _events.Add(Il2CppEvent.Subscribe(typeof(RenderPipelineManager), "endCameraRendering",
            new Action<ScriptableRenderContext, UnityEngine.Camera>(OnEndCameraRendering)));

        _initialized = true;
        HeadTrackingPlugin.Logger.LogInfo("Head tracking behaviour initialized");
    }

    private void OnRigChanged(PlayerRig? rig)
    {
        // A new level, or the old one gone: nothing from the previous room carries over.
        _pipeline.ResetState();
        _leanClamp.Reset();
        _aimFade.Reset();
        _reticle.Restore();
        _view.Invalidate();
        _pose = HeadPose.Zero;
        _hasLastCleanPosition = false;
        _rigReadyTime = Time.unscaledTime + RigWarmupSeconds;
        if (rig == null) HeadTrackingPlugin.Logger.LogInfo("Player camera gone - head tracking idle until the next level");
    }

    private void Update()
    {
        if (!_initialized || _faulted) return;

        // Hotkeys first, so a throw further down can never leave tracking latched on
        // with no way to switch it off.
        _hotkeys.ProcessInput();

        Guard(_pollGame);

        // Placement is about the window rather than about tracking, so it runs with
        // tracking toggled off, paused, or out of gameplay alike.
        Guard(_placeWindow);
    }

    private void PollGame()
    {
        _rig.Refresh();
        _state.Evaluate();

        string? connectionChange = _trackerConnection.Poll(_receiver.IsReceiving);
        if (connectionChange != null) HeadTrackingPlugin.Logger.LogInfo(connectionChange);

        int rejected = _pipeline.RejectedPoses;
        if (rejected != _loggedRejectedPoses && Time.unscaledTime >= _nextRejectedPoseLog)
        {
            HeadTrackingPlugin.Logger.LogWarning(
                $"Dropped {rejected - _loggedRejectedPoses} frame(s) whose pose overflowed to a non-finite value " +
                "and restarted smoothing - the sender is emitting values far outside any head's range");
            _loggedRejectedPoses = rejected;
            _nextRejectedPoseLog = Time.unscaledTime + RejectedPoseLogIntervalSeconds;
        }
    }

    private void OnWillRenderCanvases()
    {
        if (!_initialized || _faulted) return;
        Guard(_computeFrame);
    }

    /// <summary>
    /// Builds this frame's tracked view and moves the reticle onto the aim point.
    /// <c>Canvas.willRenderCanvases</c> also fires for a forced canvas update, so this
    /// can run more than once in a frame: the pipeline and the collision release ease
    /// advance only on the first call, and the last call - the one right before
    /// rendering - is the view that is drawn.
    /// </summary>
    private void ComputeFrame()
    {
        int frame = Time.frameCount;
        if (!_rig.IsAttached)
        {
            _view.Invalidate();
            return;
        }

        UnityEngine.Camera camera = _rig.Camera!;
        bool firstThisFrame = frame != _lastFrame;
        if (firstThisFrame)
        {
            _lastFrame = frame;
            bool enabled = _trackingEnabled && _state.InGameplay && (!_pauseOnLostFocus || _state.Focused)
                && Time.unscaledTime >= _rigReadyTime;
            _pose = _pipeline.ProcessFrame(enabled, Time.deltaTime, Time.unscaledDeltaTime);
            _aimScale = _aimFade.Update(_state.Aiming, (ulong)(Time.unscaledTimeAsDouble * 1000.0));
            _fieldOfView.Update(camera, _rig, _state.InGameplay);

            if (--_framesUntilWorldCameraRefresh <= 0)
            {
                _framesUntilWorldCameraRefresh = WorldCameraRefreshFrames;
                _rig.RefreshWorldCameras();
            }
        }

        if (!_pipeline.IsApplying || _aimScale <= 0f)
        {
            _view.Invalidate();
            _leanClamp.Reset();
            _reticle.Restore();
            _hasLastCleanPosition = false;
            return;
        }

        if (!_loggedFirstPose)
        {
            _loggedFirstPose = true;
            HeadTrackingPlugin.Logger.LogInfo(
                $"First pose applied to the camera ({(_pipeline.IsRemoteConnection ? "remote" : "local")} source)");
        }

        HeadPose applied = _pose.WithZoomAndLimits(_fieldOfView.Factor).Scaled(_aimScale);

        Transform cameraTransform = _rig.CameraTransform!;
        Vector3 cleanPosition = cameraTransform.position;
        Quaternion cleanRotation = cameraTransform.rotation;

        if (_hasLastCleanPosition && (cleanPosition - _lastCleanPosition).sqrMagnitude > TeleportMetres * TeleportMetres)
        {
            _leanClamp.Reset();
        }
        _lastCleanPosition = cleanPosition;
        _hasLastCleanPosition = true;

        RigProbe.VerifyProjection(camera, cleanPosition, cleanRotation);

        Quaternion renderRotation = applied.ComposeRotation(cleanRotation, _worldSpaceYaw);
        Vector3 wantedLean = applied.LeanWorld(cleanRotation);
        Vector3 lean = ClampLean(cleanPosition, wantedLean, camera, firstThisFrame ? Time.deltaTime : 0f);

        _view.Build(camera, cleanPosition, cleanRotation, cleanPosition + lean, renderRotation, frame);

        float aimDistance = -1f;
        RaycastHit aimHit = default;
        Vector2 offset = Vector2.zero;
        if (_showReticle)
        {
            Vector2 centre = new(_view.PixelWidth * 0.5f, _view.PixelHeight * 0.5f);
            Vector2 screenPoint = AimScreenPoint.Locate(_view, cleanPosition, cleanRotation * Vector3.forward,
                _rig.InteractionMask, out aimDistance, out aimHit);
            _reticle.MoveTo(screenPoint, centre);
            offset = screenPoint - centre;
        }
        else
        {
            _reticle.Restore();
        }

        RigProbe.Sample(_view, applied, _aimScale, _fieldOfView.Factor, aimDistance, aimHit, offset,
            _reticle.Moved, wantedLean, _leanClamp, _collisionEnabled);
    }

    private Vector3 ClampLean(Vector3 cleanPosition, Vector3 wantedLean, UnityEngine.Camera camera, float dt)
    {
        if (!_collisionEnabled || wantedLean == Vector3.zero)
        {
            _leanClamp.Reset();
            return wantedLean;
        }

        return _leanClamp.Clamp(cleanPosition, wantedLean, dt, camera.nearClipPlane, _rig.CollidableMask);
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
    {
        if (!_initialized || _faulted || !_view.IsCurrent(Time.frameCount)) return;

        _renderingCamera = camera;
        Guard(_applyToRenderingCamera);
    }

    private void ApplyToRenderingCamera()
    {
        _renderOverride.Apply(_renderingCamera!, _rig.WorldCameras, _view.InverseHeadDelta);
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
    {
        _renderOverride.EndRender(camera);
    }

    /// <summary>
    /// A throw out of a Unity callback lands in Il2CppInterop's trampoline, which
    /// reports it and does nothing more. Instead the first one is logged with its
    /// stack, the camera is handed back to the game, and the mod stops: a clean game
    /// with a clear log line beats a tracked view in an unknown state. A
    /// <c>RuntimeWrappedException</c> is an IL2CPP exception object that was thrown
    /// as-is, so the object it wraps is logged too.
    /// </summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _faulted = true;
            string wrapped = ex is System.Runtime.CompilerServices.RuntimeWrappedException rwe
                ? $" (wrapped {rwe.WrappedException?.GetType().FullName}: {rwe.WrappedException})"
                : string.Empty;
            HeadTrackingPlugin.Logger.LogError($"Head tracking stopped after an error and has handed the camera back to the game: {ex}{wrapped}");
            ReleaseCamera();
        }
    }

    private void ReleaseCamera()
    {
        _view.Invalidate();
        _renderOverride.ReleaseAll();
        _reticle.Restore();
    }

    internal void ToggleTracking()
    {
        _trackingEnabled = !_trackingEnabled;
        HeadTrackingPlugin.Logger.LogInfo($"Head tracking {(_trackingEnabled ? "ENABLED" : "DISABLED")}");
    }

    /// <summary>Rotation and position, then rotation only, then position only, then back.</summary>
    internal void CycleTrackingMode()
    {
        if (_pipeline.RotationEnabled && _pipeline.PositionEnabled)
        {
            _pipeline.PositionEnabled = false;
        }
        else if (_pipeline.RotationEnabled)
        {
            _pipeline.RotationEnabled = false;
            _pipeline.PositionEnabled = true;
        }
        else
        {
            _pipeline.RotationEnabled = true;
            _pipeline.PositionEnabled = true;
        }

        if (!_pipeline.PositionEnabled)
        {
            _leanClamp.Reset();
            _pipeline.ResetPosition();
        }

        HeadTrackingPlugin.Logger.LogInfo(
            $"Tracking mode: rotation={(_pipeline.RotationEnabled ? "on" : "off")}, " +
            $"position={(_pipeline.PositionEnabled ? "on" : "off")}");
    }

    internal void ToggleYawMode()
    {
        _worldSpaceYaw = !_worldSpaceYaw;
        HeadTrackingPlugin.Logger.LogInfo($"Yaw mode: {(_worldSpaceYaw ? "horizon-locked" : "view-local")}");
    }

    private void OnDestroy()
    {
        foreach (Il2CppEvent subscription in _events) subscription.Dispose();
        _events.Clear();
        ReleaseCamera();
        _rig.OnRigChanged -= OnRigChanged;
        _initialized = false;
        HeadTrackingPlugin.Logger.LogInfo("Head tracking behaviour destroyed");
    }
}
