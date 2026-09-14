// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ViewfinderHeadTracking.Core;
using ViewfinderHeadTracking.Utilities;

namespace ViewfinderHeadTracking.Game;

/// <summary>
/// The player's camera rig and the game components that describe what the player
/// is doing, found through the scene's <c>PlayerCharacter</c>.
///
/// Levels build the rig from a shared prefab,
/// <c>_SharedLevel_/Shared Level Controller/Level Player Variant/Player Body/Head/ScreenShaker/Player World Camera</c>;
/// the hub builds its own, <c>_SharedHub_/Hub Player Variant/...</c>, which has no
/// <c>EquipmentManager</c>. Both carry <c>PlayerCharacter</c>,
/// <c>PlayerCameraController</c>, <c>PlayerHeadRotation</c>, <c>CutsceneHeadMove</c>
/// and <c>Interactor</c> on the variant root. The camera is taken from
/// <c>PlayerCharacter.WorldCamera</c> rather than <c>Camera.main</c>, so nothing
/// else the game tags MainCamera can pull tracking onto it.
///
/// <c>PlayerCameraController.playerWorldCameras</c> is the game's own list of the
/// cameras that draw the world for the player. It held that one camera on every level
/// visited and in the hub, but the render hook drives every camera in it rather than
/// assuming a count.
/// </summary>
internal sealed class PlayerRig
{
    private const int SearchRetryFrames = 30;

    private Func<object, UnityEngine.Camera?> _worldCamera = null!;
    private Func<object, object?> _headRotationOf = null!;
    private Func<object, object?> _cutsceneHeadMoveOf = null!;
    private Func<object, object?> _equipmentOf = null!;
    private Func<object, object?> _movementOf = null!;
    private Func<object, object?> _motorOf = null!;
    private Func<object, object?> _worldCameras = null!;
    private Func<object, float> _fovSetting = null!;
    private Func<object, int> _headState = null!;
    private Func<object, int> _cutsceneState = null!;
    private Func<object, int> _equipmentAction = null!;
    private Func<object, int> _interactionMask = null!;
    private Func<object, int> _collidableMask = null!;

    private int _framesUntilSearch;
    private int _loggedCameraId;

    private object? _cameraController;
    private object? _headRotation;
    private object? _cutsceneHeadMove;
    private object? _equipment;
    private object? _interactor;
    private object? _motor;

    private readonly List<UnityEngine.Camera> _worldCameraList = new();

    internal UnityEngine.Camera? Camera { get; private set; }
    internal Transform? CameraTransform { get; private set; }

    /// <summary>Fired when the rig is found, replaced or lost (null).</summary>
    internal event Action<PlayerRig?>? OnRigChanged;

    /// <summary>Compiles every accessor the rig reads. Throws naming the first one missing.</summary>
    internal void ResolveMembers()
    {
        _worldCamera = GameMembers.CompileGetter<UnityEngine.Camera?>(GameTypes.PlayerCharacter, "WorldCamera");
        _headRotationOf = GameMembers.CompileGetter<object?>(GameTypes.PlayerCharacter, "HeadRotation");
        _cutsceneHeadMoveOf = GameMembers.CompileGetter<object?>(GameTypes.PlayerCharacter, "CutsceneHeadMove");
        _equipmentOf = GameMembers.CompileGetter<object?>(GameTypes.PlayerCharacter, "EquipmentManager");
        _movementOf = GameMembers.CompileGetter<object?>(GameTypes.PlayerCharacter, "Movement");
        _motorOf = GameMembers.CompileGetter<object?>(GameTypes.PlayerMovement, "Motor");
        _worldCameras = GameMembers.CompileGetter<object?>(GameTypes.PlayerCameraController, "playerWorldCameras");
        _fovSetting = GameMembers.CompileGetter<float>(GameTypes.PlayerCameraController, "fovSetting");
        _headState = GameMembers.CompileGetter<int>(GameTypes.PlayerHeadRotation, "CurrentHeadState");
        _cutsceneState = GameMembers.CompileGetter<int>(GameTypes.CutsceneHeadMove, "HeadState");
        _equipmentAction = GameMembers.CompileMethod<int>(GameTypes.EquipmentManager, "GetEquipmentAction");
        _interactionMask = GameMembers.CompileGetter<int>(GameTypes.Interactor, "layerMask");
        _collidableMask = GameMembers.CompileGetter<int>(GameTypes.KinematicCharacterMotor, "CollidableLayers");
    }

    internal bool IsAttached => Camera != null;

    /// <summary>
    /// Keeps the rig current. A destroyed camera means the scene went, so the rig is
    /// dropped and searched for again on an interval.
    /// </summary>
    internal void Refresh()
    {
        if (Camera != null) return;

        if (CameraTransform is not null)
        {
            Detach();
        }

        if (--_framesUntilSearch > 0) return;
        _framesUntilSearch = SearchRetryFrames;

        object? character = GameMembers.FindObjectOfType(GameTypes.PlayerCharacter);
        if (character == null) return;

        UnityEngine.Camera? camera = _worldCamera(character);
        if (camera == null) return;

        // System exceptions only: UnityEngine's exception types are IL2CPP objects at
        // runtime, and throwing one arrives as a RuntimeWrappedException with the
        // message gone.
        GameObject root = ((Component)character).gameObject;
        _cameraController = GameMembers.GetComponent(root, GameTypes.PlayerCameraController)
            ?? throw new InvalidOperationException($"PlayerCameraController missing beside PlayerCharacter on {root.name}");
        _interactor = GameMembers.GetComponent(root, GameTypes.Interactor)
            ?? throw new InvalidOperationException($"Interactor missing beside PlayerCharacter on {root.name}");
        _headRotation = _headRotationOf(character)
            ?? throw new InvalidOperationException("PlayerCharacter.HeadRotation is null");
        _cutsceneHeadMove = _cutsceneHeadMoveOf(character)
            ?? throw new InvalidOperationException("PlayerCharacter.CutsceneHeadMove is null");
        object movement = _movementOf(character)
            ?? throw new InvalidOperationException("PlayerCharacter.Movement is null");
        _motor = _motorOf(movement)
            ?? throw new InvalidOperationException("PlayerMovement.Motor is null");

        // Null on the hub's rig: there is no camera or photo to raise there.
        _equipment = _equipmentOf(character);

        Camera = camera;
        CameraTransform = camera.transform;
        RefreshWorldCameras();

        int id = camera.GetInstanceID();
        if (id != _loggedCameraId)
        {
            _loggedCameraId = id;
            var names = new List<string>();
            foreach (UnityEngine.Camera worldCamera in _worldCameraList) names.Add(worldCamera.name);
            HeadTrackingPlugin.Logger.LogInfo(
                $"Head tracking camera: {TransformPath.GetFullPath(CameraTransform)} " +
                $"(near {camera.nearClipPlane:F3}, far {camera.farClipPlane:F0}, " +
                $"world cameras [{string.Join(", ", names)}], equipment {(_equipment != null ? "present" : "none")}, " +
                $"interaction mask 0x{InteractionMask:X8}, character collision mask 0x{CollidableMask:X8})");
        }

        OnRigChanged?.Invoke(this);
    }

    private void Detach()
    {
        Camera = null;
        CameraTransform = null;
        _cameraController = null;
        _headRotation = null;
        _cutsceneHeadMove = null;
        _equipment = null;
        _interactor = null;
        _motor = null;
        _worldCameraList.Clear();
        _framesUntilSearch = SearchRetryFrames;
        OnRigChanged?.Invoke(null);
    }

    /// <summary>The cameras that draw the world for the player, as last read.</summary>
    internal IReadOnlyList<UnityEngine.Camera> WorldCameras => _worldCameraList;

    /// <summary>
    /// Re-reads the game's list of player world cameras, skipping destroyed entries.
    /// Throws when the list does not hold the camera tracking is computed for: the
    /// render hook writes only to cameras in it, so the view would stay still while
    /// the reticle moved to where a turned view would put it.
    /// </summary>
    internal void RefreshWorldCameras()
    {
        _worldCameraList.Clear();
        // Il2CppReferenceArray<Camera> at runtime, which the NuGet Camera cannot name at
        // compile time; it enumerates.
        if (_worldCameras(_cameraController!) is not IEnumerable cameras)
        {
            throw new InvalidOperationException("PlayerCameraController.playerWorldCameras is not a camera list");
        }

        bool holdsTrackedCamera = false;
        foreach (object entry in cameras)
        {
            if (entry is not UnityEngine.Camera camera || camera == null) continue;
            _worldCameraList.Add(camera);
            if (camera == Camera) holdsTrackedCamera = true;
        }

        if (!holdsTrackedCamera)
        {
            throw new InvalidOperationException(
                $"PlayerCameraController.playerWorldCameras does not hold {TransformPath.GetFullPath(CameraTransform!)}");
        }
    }

    /// <summary>The player's field-of-view setting, in vertical degrees.</summary>
    internal float FovSetting => _fovSetting(_cameraController!);

    /// <summary><c>PlayerHeadRotation.HeadState</c>: FreeLook, LimitedLook, LookAt or DontUpdate.</summary>
    internal int HeadState => _headState(_headRotation!);

    /// <summary><c>CutsceneHeadMove.HeadStates</c>: None, MovingToPoint or HeldAtPoint.</summary>
    internal int CutsceneHeadState => _cutsceneState(_cutsceneHeadMove!);

    /// <summary><c>EquipmentManager.EquipmentAction</c> as its integer value, or -1 on a rig without equipment.</summary>
    internal int EquipmentAction => _equipment == null ? -1 : _equipmentAction(_equipment);

    /// <summary>The layers the game's own interaction ray is cast against.</summary>
    internal int InteractionMask => _interactionMask(_interactor!);

    /// <summary>The layers the player's character controller collides with: the level's solid geometry.</summary>
    internal int CollidableMask => _collidableMask(_motor!);
}
