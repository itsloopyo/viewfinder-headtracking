// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CameraUnlock.Core.Config;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using ViewfinderHeadTracking.Configuration;
using ViewfinderHeadTracking.Tracking;
using ViewfinderHeadTracking.Utilities;
using Object = UnityEngine.Object;

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
    private ConfigOwner<ViewfinderConfig>? _configOwner;

    public override void Load()
    {
        Logger = Log;

        ViewfinderConfig config = LoadConfig();

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
        _behaviour.Initialize(_receiver, pipeline, config, SaveConfig);

        _receiver.Log = msg => Logger.LogInfo(msg);
        int port = config.UdpPort;
        if (_receiver.Start(port))
        {
            Logger.LogInfo($"Listening for tracker data on UDP port {port}");
        }

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded - tracking is " +
                       $"{(config.EnableOnStartup ? "ENABLED" : "DISABLED")} on startup");
    }

    /// <summary>
    /// The settings live in BepInEx\config\CameraUnlock.ini, read and written by core's config
    /// owner, with rows set to default following the player's Defaults.ini. Nothing is bound on
    /// the plugin's Config, so ConfigurationManager does not list them. While CameraUnlock.ini is
    /// absent the owner imports the plugin's .cfg, the file every earlier build read, through the
    /// frozen reader on a ConfigFile of its own, and never writes that file.
    ///
    /// The mod has nothing on screen to show a message with, so the owner's messages for the
    /// player go to the log beside its other lines.
    /// </summary>
    private ViewfinderConfig LoadConfig()
    {
        ConfigOwnerOptions<ViewfinderConfig> options =
            ViewfinderConfig.Options(ConfigPath, Config.ConfigFilePath, DefaultsFile.PerUser());
        options.StatusSink = message => Logger.LogWarning(message);
        _configOwner = new ConfigOwner<ViewfinderConfig>(options);

        ConfigLoadResult<ViewfinderConfig> loaded = _configOwner.Load();

        // The owner writes each diagnostic as "<path>: <description>" among lines that only
        // report what it did, so the complaints are picked out by their text.
        var complaints = new HashSet<string>();
        foreach (CanonicalDiagnostic diagnostic in loaded.Diagnostics)
        {
            complaints.Add(ConfigPath + ": " + diagnostic.Describe());
        }
        bool usable = loaded.Status == ConfigLoadStatus.Canonical
                      || loaded.Status == ConfigLoadStatus.Migrated
                      || loaded.Status == ConfigLoadStatus.Created;
        foreach (string line in loaded.Log)
        {
            if (usable && !complaints.Contains(line)) Logger.LogInfo(line);
            else Logger.LogWarning(line);
        }
        Logger.LogInfo($"Config {ConfigPath}: {loaded.Status}");
        return loaded.Config;
    }

    private static string ConfigPath => Path.Combine(Paths.ConfigPath, "CameraUnlock.ini");

    /// <summary>
    /// Called after the new value is already applied. A save that fails is logged and the
    /// session keeps the new value.
    /// </summary>
    private void SaveConfig(Action<ViewfinderConfig> change)
    {
        ConfigSaveResult saved = _configOwner!.Save(change);
        if (saved.Status == ConfigSaveStatus.Saved)
        {
            // A row that held default and now holds a value, so it stops following
            // Defaults.ini in this game.
            foreach (string line in saved.Log) Logger.LogInfo(line);
            return;
        }
        foreach (string line in saved.Log) Logger.LogWarning(line);
        Logger.LogWarning($"{ConfigPath}: {saved.Status}: {saved.Reason} The change applies to this session only.");
    }

    /// <summary>
    /// The shared pipeline at the tracker's own scale: no sensitivity, deadzone or axis
    /// inversion, because the tracker owns pose shaping and the axis signs are applied
    /// at the engine boundary in <see cref="HeadPose"/>.
    /// </summary>
    private static TrackingPipeline CreatePipeline(OpenTrackReceiver receiver, ViewfinderConfig config)
    {
        return new TrackingPipeline(
            receiver,
            new TrackingProcessor
            {
                LocalSmoothing = config.LocalSmoothing,
                RemoteSmoothing = config.RemoteSmoothing,
                Sensitivity = SensitivitySettings.Default,
                Deadzone = DeadzoneSettings.None
            },
            new PoseInterpolator(),
            new PositionProcessor
            {
                Settings = new PositionSettings(
                    1f, 1f, 1f,
                    config.Position.LimitX,
                    config.Position.LimitY,
                    config.Position.LimitYDown,
                    config.Position.LimitZ,
                    config.Position.LimitZBack,
                    localSmoothing: config.LocalSmoothing,
                    remoteSmoothing: config.RemoteSmoothing,
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
