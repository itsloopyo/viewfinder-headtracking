// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ViewfinderHeadTracking.Core;
using ViewfinderHeadTracking.Utilities;

namespace ViewfinderHeadTracking.Game;

/// <summary>
/// Decides, once per frame, whether head tracking may move the view.
///
/// Gameplay is every one of these at once:
/// <list type="bullet">
/// <item><c>GameStateManager.CurrentState</c> is <c>Game</c>. The pause menu pushes
/// OverlayMenu and a level restart runs through Cutscene; every other state the
/// stack can hold (Popup, PopupWithPrompts, CutsceneAllowHeadTurn, DemoEnd,
/// DebugMenu) is a screen or a sequence rather than play.</item>
/// <item>The player rig exists and its world camera is enabled. The state stack
/// reads Game on the new-game loading screen, before the level's player exists.</item>
/// <item><c>PlayerHeadRotation</c> is in FreeLook or LimitedLook. LookAt and
/// DontUpdate are the game taking the head off the player to point it somewhere.</item>
/// <item><c>CutsceneHeadMove</c> is not moving or holding the head at a point.</item>
/// <item>No confirmation dialog (<c>PopupMenu.IsShowing</c>) or message popup
/// (<c>PopupManager.popupOpen</c>) is up. These open over gameplay without
/// touching the state stack - "Quit to menu?" leaves it reading Game.</item>
/// <item>The cursor is locked. The game releases it for the main menu, the pause menu
/// and its dialogs, so a pointer-driven screen this list does not name is caught too.</item>
/// <item><c>Time.timeScale</c> is above zero.</item>
/// </list>
///
/// Lining up a shot is separate from all of that. While the instant camera or a
/// photo is raised to the eye the frame on screen IS the shot: the photo is cut
/// from the clean camera's frustum and placed from it, so a head-tracked view would
/// show the player one framing and deliver another. <see cref="Aiming"/> is true
/// for those equipment actions and for <c>PlayerHeadRotation</c>'s LimitedLook, and
/// the pose is faded out rather than cut. LimitedLook is the state
/// <c>MountedCamera.OnAiming</c> puts the head in while the player looks through a
/// mounted camera, which is the same framing problem, and any other use of it is the
/// game fencing the view in.
/// </summary>
internal sealed class GameplayState
{
    private const int SearchRetryFrames = 30;

    private readonly PlayerRig _rig;

    private Func<object?> _stateManager = null!;
    private Func<object, int> _currentState = null!;
    private Func<object, bool> _popupMenuShowing = null!;
    private Func<object, bool> _popupManagerOpen = null!;
    private Type _gameStateType = null!;
    private Type _headStateType = null!;
    private Type _equipmentActionType = null!;
    private int _gameState;
    private int _freeLook;
    private int _limitedLook;
    private int _cutsceneHeadIdle;
    private readonly HashSet<int> _aimActions = new();

    private UnityEngine.Object? _popupMenu;
    private UnityEngine.Object? _popupManager;
    private int _framesUntilPopupSearch;

    private (bool, bool, int, int, bool, int, int, int, bool, bool, float, bool) _lastSignals;
    private bool _describedAny;

    internal GameplayState(PlayerRig rig)
    {
        _rig = rig;
    }

    internal bool InGameplay { get; private set; }
    internal bool Aiming { get; private set; }
    internal bool Focused { get; private set; } = true;

    /// <summary>Whether a STATE line is written whenever a signal changes.</summary>
    internal bool DiagnosticLogging { get; set; }

    /// <summary>Compiles the state accessors and resolves the enum values the gate compares against.</summary>
    internal void ResolveMembers()
    {
        _stateManager = GameMembers.CompileStaticGetter<object?>(GameTypes.GameStateManager, "Instance");
        _currentState = GameMembers.CompileGetter<int>(GameTypes.GameStateManager, "CurrentState");
        _gameStateType = GameMembers.RequireGetter(GameTypes.GameStateManager, "CurrentState").ReturnType;
        _gameState = EnumValue(_gameStateType, "Game");

        _headStateType = GameMembers.RequireGetter(GameTypes.PlayerHeadRotation, "CurrentHeadState").ReturnType;
        _freeLook = EnumValue(_headStateType, "FreeLook");
        _limitedLook = EnumValue(_headStateType, "LimitedLook");

        Type cutsceneState = GameMembers.RequireGetter(GameTypes.CutsceneHeadMove, "HeadState").ReturnType;
        _cutsceneHeadIdle = EnumValue(cutsceneState, "None");

        _popupMenuShowing = GameMembers.CompileGetter<bool>(GameTypes.PopupMenu, "IsShowing");
        _popupManagerOpen = GameMembers.CompileGetter<bool>(GameTypes.PopupManager, "popupOpen");

        _equipmentActionType = GameMembers.RequireMethod(GameTypes.EquipmentManager, "GetEquipmentAction").ReturnType;
        foreach (string name in new[]
                 {
                     "CameraToCameraAim", "CameraAim", "CameraAimToCamera", "CameraFire",
                     "PhotosToPhotosAim", "PhotosAim", "PhotosAimToPhotos", "PhotosFire",
                     "GenericToGenericAim", "GenericAim", "GenericAimToGeneric"
                 })
        {
            _aimActions.Add(EnumValue(_equipmentActionType, name));
        }
    }

    private static int EnumValue(Type enumType, string name)
    {
        if (!Enum.IsDefined(enumType, name)) throw new MissingFieldException(enumType.FullName, name);
        return Convert.ToInt32(Enum.Parse(enumType, name));
    }

    internal void Evaluate()
    {
        Focused = Application.isFocused;

        object? manager = _stateManager();
        int state = manager is UnityEngine.Object unityObject && unityObject != null ? _currentState(manager) : -1;

        bool cameraLive = _rig.IsAttached && _rig.Camera!.isActiveAndEnabled;
        int headState = cameraLive ? _rig.HeadState : -1;
        int cutsceneHead = cameraLive ? _rig.CutsceneHeadState : -1;
        int action = cameraLive ? _rig.EquipmentAction : -1;
        bool popup = PopupShowing();
        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
        float timeScale = Time.timeScale;

        InGameplay = state == _gameState
                     && cameraLive
                     && (headState == _freeLook || headState == _limitedLook)
                     && cutsceneHead == _cutsceneHeadIdle
                     && !popup
                     && cursorLocked
                     && timeScale > 0f;
        Aiming = _aimActions.Contains(action) || headState == _limitedLook;

        if (!DiagnosticLogging) return;

        // Compared as values so an unchanged frame builds no strings. timeScale is
        // compared at the precision it is logged at.
        Scene scene = SceneManager.GetActiveScene();
        var signals = (InGameplay, Aiming, scene.handle, state, cameraLive, headState, cutsceneHead, action,
            popup, cursorLocked, Mathf.Round(timeScale * 100f), Focused);
        if (_describedAny && signals.Equals(_lastSignals)) return;
        _describedAny = true;
        _lastSignals = signals;

        HeadTrackingPlugin.Logger.LogInfo(
            $"STATE gameplay={InGameplay} aiming={Aiming} scene='{scene.name}' " +
            $"state={Describe(_gameStateType, state)} camera={cameraLive} " +
            $"head={Describe(_headStateType, headState)} cutsceneHead={cutsceneHead} " +
            $"equipment={Describe(_equipmentActionType, action)} popup={popup} " +
            $"cursorLocked={cursorLocked} timeScale={timeScale:F2} focused={Focused}");
    }

    /// <summary>
    /// Whether either of the game's popups is up. Both live on the persistent game
    /// controller, so they are found once and searched for again only if destroyed.
    /// Until they exist - the boot scene - no popup can be showing.
    /// </summary>
    private bool PopupShowing()
    {
        if (_popupMenu == null || _popupManager == null)
        {
            if (--_framesUntilPopupSearch > 0) return false;
            _framesUntilPopupSearch = SearchRetryFrames;
            _popupMenu = (UnityEngine.Object?)GameMembers.FindObjectOfType(GameTypes.PopupMenu);
            _popupManager = (UnityEngine.Object?)GameMembers.FindObjectOfType(GameTypes.PopupManager);
            if (_popupMenu == null || _popupManager == null) return false;
        }

        return _popupMenuShowing(_popupMenu) || _popupManagerOpen(_popupManager);
    }

    private static string Describe(Type enumType, int value)
    {
        return value < 0 ? "(none)" : Enum.GetName(enumType, value) ?? value.ToString();
    }
}
