// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections.Generic;
using System.Reflection;

namespace ViewfinderHeadTracking.Utilities;

/// <summary>
/// The Viewfinder types the mod reflects into, resolved once from the interop
/// assemblies BepInEx generated. The mod never references them at compile time,
/// so the build stays game-independent.
///
/// Every one of them is required: without the player rig, the game state stack
/// and the equipment state there is no way to tell gameplay from a menu or a
/// photo being lined up, so a missing type leaves the mod dormant rather than
/// tracking blind. <see cref="Resolve"/> names each one it could not find.
/// </summary>
internal static class GameTypes
{
    internal static Type PlayerCharacter = null!;
    internal static Type PlayerCameraController = null!;
    internal static Type PlayerHeadRotation = null!;
    internal static Type CutsceneHeadMove = null!;
    internal static Type EquipmentManager = null!;
    internal static Type GameStateManager = null!;
    internal static Type ReticleController = null!;
    internal static Type Interactor = null!;
    internal static Type PlayerMovement = null!;
    internal static Type KinematicCharacterMotor = null!;
    internal static Type PopupMenu = null!;
    internal static Type PopupManager = null!;

    /// <summary>Resolves every type, returning the names of those that are missing.</summary>
    internal static List<string> Resolve()
    {
        var missing = new List<string>();
        PlayerCharacter = Find("PlayerCharacter", missing);
        PlayerCameraController = Find("PlayerCameraController", missing);
        PlayerHeadRotation = Find("PlayerHeadRotation", missing);
        CutsceneHeadMove = Find("CutsceneHeadMove", missing);
        EquipmentManager = Find("EquipmentManager", missing);
        GameStateManager = Find("GameStateManager", missing);
        ReticleController = Find("ReticleController", missing);
        Interactor = Find("Interactor", missing);
        PlayerMovement = Find("PlayerMovement", missing);
        KinematicCharacterMotor = Find("KinematicCharacterController.KinematicCharacterMotor", missing);
        PopupMenu = Find("PopupMenu", missing);
        PopupManager = Find("PopupManager", missing);
        return missing;
    }

    private static Type Find(string fullName, List<string> missing)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, false);
            if (type != null) return type;
        }

        missing.Add(fullName);
        return null!;
    }
}
