// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using ViewfinderHeadTracking.Configuration;
using ViewfinderHeadTracking.Tracking;
using ViewfinderHeadTracking.Utilities;

namespace ViewfinderHeadTracking.Core;

/// <summary>
/// BepInEx IL2CPP entry point. Builds the tracking pipeline, hosts the behaviour on
/// a scene-independent GameObject, and starts listening for the tracker.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class HeadTrackingPlugin : BasePlugin
{
    internal const string PluginGuid = "com.cameraunlock.viewfinder.headtracking";
    internal const string PluginName = "Viewfinder Head Tracking";
    internal const string PluginVersion = "0.0.0";

    internal static ManualLogSource Logger { get; private set; } = null!;

    // Static so IL2CPP's GC cannot collect the managed graph behind the injected behaviour.
    private static GameObject? _hostObject;
    private static HeadTrackingBehaviour? _behaviour;

    private OpenTrackReceiver? _receiver;

    public override void Load()
    {
        Logger = Log;

        var config = new PluginConfig();
        config.Initialize(Config);

        // Every game type is checked before anything is hooked. A build that has
        // renamed one of them gets an untouched game and a log line naming what moved,
        // rather than tracking with no idea whether the player is in a menu.
        List<string> missing = GameTypes.Resolve();
        if (missing.Count > 0)
        {
            Logger.LogError(
                $"{PluginName} v{PluginVersion} is inactive: this Viewfinder build does not have {string.Join(", ", missing)}. " +
                "The game runs unmodified; check for an updated mod.");
            return;
        }

        _receiver = new OpenTrackReceiver();
        TrackingPipeline pipeline = CreatePipeline(_receiver, config);

        ClassInjector.RegisterTypeInIl2Cpp<HeadTrackingBehaviour>();
        _hostObject = new GameObject("ViewfinderHeadTracking");
        _hostObject.hideFlags = HideFlags.DontSave;
        Object.DontDestroyOnLoad(_hostObject);
        _behaviour = _hostObject.AddComponent<HeadTrackingBehaviour>();
        _behaviour.Initialize(_receiver, pipeline, config);

        _receiver.Log = msg => Logger.LogInfo(msg);
        int port = config.UdpPort.Value;
        if (_receiver.Start(port))
        {
            Logger.LogInfo($"Listening for tracker data on UDP port {port}");
        }

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded - tracking is " +
                       $"{(config.EnabledOnStartup.Value ? "ENABLED" : "DISABLED")} on startup");
    }

    /// <summary>
    /// The shared pipeline at the tracker's own scale: no sensitivity, deadzone or axis
    /// inversion, because the tracker owns pose shaping and the axis signs are applied
    /// at the engine boundary in <see cref="HeadPose"/>.
    /// </summary>
    private static TrackingPipeline CreatePipeline(OpenTrackReceiver receiver, PluginConfig config)
    {
        return new TrackingPipeline(
            receiver,
            new TrackingProcessor
            {
                LocalSmoothing = config.LocalSmoothing.Value,
                RemoteSmoothing = config.RemoteSmoothing.Value,
                Sensitivity = SensitivitySettings.Default,
                Deadzone = DeadzoneSettings.None
            },
            new PoseInterpolator(),
            new PositionProcessor
            {
                Settings = new PositionSettings(
                    1f, 1f, 1f,
                    config.PositionLimitX.Value,
                    config.PositionLimitY.Value,
                    config.PositionLimitYDown.Value,
                    config.PositionLimitZ.Value,
                    config.PositionLimitZBack.Value,
                    localSmoothing: config.LocalSmoothing.Value,
                    remoteSmoothing: config.RemoteSmoothing.Value,
                    invertX: false, invertY: false, invertZ: false)
            },
            new PositionInterpolator());
    }

    public override bool Unload()
    {
        _receiver?.Dispose();
        _behaviour = null;
        if (_hostObject != null)
        {
            Object.Destroy(_hostObject);
            _hostObject = null;
        }

        Logger.LogInfo($"{PluginName} unloaded.");
        return true;
    }
}
