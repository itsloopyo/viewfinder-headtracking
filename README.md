# Viewfinder Head Tracking

![Viewfinder running with this mod](https://raw.githubusercontent.com/itsloopyo/viewfinder-headtracking/main/assets/readme-clip.gif)

An unofficial head tracking mod for Viewfinder that moves the view with your head while your mouse or controller keeps aiming, driven by a webcam, phone, or any OpenTrack compatible tracker, with no VR headset required.

## Features

- **Decoupled look and aim** - head tracking moves the view, your mouse or controller keeps the aim
- **6DOF tracking** - yaw, pitch and roll plus positional lean, peek and duck
- **Works with any OpenTrack compatible tracker** - free options available for PC, iOS and Android

## Requirements

- [Viewfinder](https://store.steampowered.com/app/1382070/Viewfinder/) on Windows. Built and tested against the Steam release, version `1.1.4` as shown on the main menu. Copies from other stores have not been run here; if you have one, point `install.cmd` at its folder as shown below.
- A tracking source: [OpenTrack](https://github.com/opentrack/opentrack/releases) with a webcam, a phone app, or any other tracker that sends the OpenTrack UDP protocol.
- Windows 10 or 11, 64-bit.

BepInEx 6 (IL2CPP) is bundled with the installer and set up for you; there is nothing to download separately.

The first launch after installing takes noticeably longer than usual. BepInEx generates its interop assemblies from the game's IL2CPP metadata once, and downloads a set of Unity reference libraries to do it, so that launch needs a working internet connection. Every launch after it is normal speed.

## Installation

### Lopari

Once this mod is available in Lopari, download [Lopari](https://lopari.app), choose **Viewfinder**, and click
**Play with head tracking**.

### Standalone Installer

1. Download `ViewfinderHeadTracking-vX.Y.Z-installer.zip` from the [Releases page](https://github.com/itsloopyo/viewfinder-headtracking/releases).
2. Extract it anywhere.
3. Double-click `install.cmd`. It finds the Steam copy of the game, lays down BepInEx and copies the mod in.
4. Configure OpenTrack (or your phone app) to send UDP to `127.0.0.1:4242`. See [Setting Up OpenTrack](#setting-up-opentrack).
5. Launch the game.

**Success looks like:** a `BepInEx` folder and a `winhttp.dll` next to `Viewfinder.exe`, and a `BepInEx/LogOutput.log` after the first launch containing `[Info   :Viewfinder Head Tracking] Listening for tracker data on UDP port 4242`.

**If the installer cannot find your game**, tell it where the game is. Either pass the folder as an argument:

```powershell
.\install.cmd "D:\Games\Viewfinder"
```

or set the override environment variable before running it:

```powershell
$env:VIEWFINDER_PATH = "D:\Games\Viewfinder"
.\install.cmd
```

The installer patches one copy of the game per run. If you have Viewfinder installed in more than one place, run it again with the other folder as the argument.

Mod managers do not deploy this mod. The payload is a loader plus plugin DLLs that have to land at the game root, and Vortex deploys only into the one subtree its per-game extension names; it ships no Viewfinder extension at all. Use `install.cmd`.

### Manual Installation

If you would rather not run the installer, the ZIP contains everything:

1. Extract `vendor/bepinex/BepInEx_UnityIL2CPP_x64.zip` into the game folder (the one holding `Viewfinder.exe`).
2. Copy `plugins/ViewfinderHeadTracking.dll`, `plugins/CameraUnlock.Core.dll` and `plugins/CameraUnlock.Core.Unity.dll` into `BepInEx/plugins`.

On Steam the game folder is `steamapps\common\Viewfinder` inside your Steam library.

## Setting Up OpenTrack

1. Install [OpenTrack](https://github.com/opentrack/opentrack/releases).
2. Set **Input** to your tracker.
3. Set **Output** to **UDP over network**.
4. In the output options set the address to `127.0.0.1` and the port to `4242`.
5. Center yourself with OpenTrack's own Center bind, then press **Start**.

Centering is done in the tracker: OpenTrack's Center bind, the CENTER button in a phone app, or SteamVR's reset.

### VR Headset Setup

1. Connect the headset over Air Link, Virtual Desktop or a link cable.
2. Start SteamVR.
3. Set OpenTrack's **Input** to the SteamVR tracker.
4. Leave **Output** on UDP `127.0.0.1`, port `4242`.

### Webcam Setup

Set OpenTrack's **Input** to the **neuralnet** tracker. It works from a plain webcam, with no markers, clips or IR hardware.

### Phone App Setup

This mod accepts one thing: the OpenTrack UDP protocol on port `4242`. A phone app is usable here if it sends that protocol itself, or ships a PC-side companion that does. Check your app against that first.

For an app that does send it, the deciding factor is how much filtering it does before the packet leaves the phone:

- An app that filters on-device can point straight at your PC's LAN address on port `4242`. I made [Headcam](https://headcam.app) so decent tracking was free for anybody with a phone already in their pocket; it filters on-device, so it can send direct. Any app that filters as well works identically.
- A raw or lightly filtered feed will jitter if you send it direct, because the mod's smoothing is sized to take the edge off a clean signal rather than to rescue a noisy one. Send that app into OpenTrack on the PC instead and let OpenTrack forward to the game, so its filters and curves can clean the feed up first.

The test rather than the list: try direct, hold your head still, and if the view drifts or shakes, route it through OpenTrack.

A tracker sending from another device on the network gets `RemoteSmoothing` rather than `LocalSmoothing`. That is decided by the address the packets arrive from, not by which machine they started on, so an OpenTrack instance on this PC sending to `192.168.x.x` instead of `127.0.0.1` also counts as remote.

## Controls

Two equivalent binding sets. Use whichever your keyboard has.

| Action              | Nav-cluster | Chord          |
|---------------------|-------------|----------------|
| Toggle tracking     | `End`       | `Ctrl+Shift+Y` |
| Cycle tracking mode | `Page Up`   | `Ctrl+Shift+G` |
| Toggle yaw mode     | `Page Down` | `Ctrl+Shift+H` |

**Cycle tracking mode** steps through three positions in order:

1. Rotation and position, the normal state. Your head turns the view and leaning moves it.
2. Rotation only. Leaning does nothing, turning still works.
3. Position only. The view stops turning with your head but still leans.

The third press returns to the first.

**Toggle yaw mode** switches between horizon-locked yaw, where turning your head rotates the view about the world's up axis and the horizon stays level, and view-local yaw, where it rotates about the view's own up axis. Horizon-locked is the default.

## Configuration

Settings live in `BepInEx/config/com.cameraunlock.viewfinder.headtracking.cfg`, created on the first launch. Edit it with the game closed; the config is read at startup. Every setting is below with its default. The file itself carries more per-entry detail than this, including each setting's type and its accepted range.

```ini
[Network]
# UDP port the mod listens on for OpenTrack protocol packets. 4242 is the OpenTrack default.
UdpPort = 4242

[General]
# Enable head tracking automatically when the game starts.
EnabledOnStartup = true
# Move the game's reticle onto the point you are really aiming at while tracking.
# false leaves its position unchanged.
ShowReticle = true
# true = horizon-locked yaw, turning your head rotates the view about the world's up
# axis. false = the view's own up axis.
WorldSpaceYaw = true
# Stop applying head tracking while the game window is not focused.
PauseOnLostFocus = true
# Write the camera rig, the aim geometry and the game-state signals to BepInEx/LogOutput.log.
# Verbose; turn it on when reporting a problem.
DiagnosticLogging = false

[Smoothing]
# Smoothing for a tracker sending to the loopback address (127.0.0.1).
# 0 = lightest, 1 = heaviest. Covers rotation and position.
LocalSmoothing = 0
# Smoothing for a tracker sending from any other address: another device such as a phone,
# and also a tracker on this PC that sends to its network address rather than 127.0.0.1.
# 0 = lightest, 1 = heaviest. Covers rotation and position.
RemoteSmoothing = 0.15

[Position]
# Whether leaning moves the view.
PositionEnabled = true
# Maximum sideways lean in metres, applied both ways.
LimitX = 0.3
# Maximum upward and downward movement in metres.
LimitY = 0.2
LimitYDown = 0.2
# Maximum forward lean in metres.
LimitZ = 0.4
# Maximum backward lean. Deliberately tighter than LimitZ so the view cannot pull
# back through the player.
LimitZBack = 0.1

[Collision]
# Sweep the lean against the level so leaning into a wall cannot put the view inside it.
CollisionEnabled = true
# How far off a surface the view is held, in metres. Must be larger than the camera's
# near clip distance, or the wall is still not drawn and you still see through it.
# The mod raises it and says so in the log if it is set too small.
CollisionRadius = 0.12
# How gently the lean opens back up once an obstruction clears. Tightening is always
# instant. 0.9 is about a fifth of a second.
CollisionReleaseSmoothing = 0.9

[Hotkeys]
# The Ctrl+Shift chords are fixed and always work alongside these.
ToggleKey = End
CycleTrackingModeKey = PageUp
YawModeKey = PageDown
```

Field of view is the game's own setting. The mod reads the field of view the game is rendering on every frame, so head tracking moves the view by the same amount on screen at any setting, and the reticle stays on target.

## Troubleshooting

Read `BepInEx/LogOutput.log` first. It sits in the `BepInEx` folder next to `Viewfinder.exe`, is rewritten on every launch, and the mod's lines are tagged `[Info   :Viewfinder Head Tracking]`. Whenever the mod loads it records its version. Once it is running it also records the UDP port it listens on and the first tracker packet it accepts.

**Mod not loading** (no `Viewfinder Head Tracking` lines in the log, or no log at all)

- Check `winhttp.dll` and the `BepInEx` folder are next to `Viewfinder.exe`. If they are not, `install.cmd` did not find the game. Run it again and pass the game folder as an argument.
- If the log stops before any plugin loads, the first launch may not have reached the internet, so BepInEx could not download its Unity reference libraries. Connect and launch again.
- If the log says the mod is inactive because this Viewfinder build does not have a type it names, the game has been updated in a way the mod does not know yet. The game runs unmodified; check the Releases page for an updated mod.

**No tracking response** (the log is there, the view does not move)

- If the log says the port could not be bound: `AddressAlreadyInUse` means another program is already listening on it, most often a second game with a head tracking mod that is still running. Close it and this mod takes the port over on its own within about half a second, so there is no need to restart Viewfinder. It retries twice a second for as long as the game is running, and writes a line every 30 seconds saying it is still waiting.
- Any other bind error means nothing is holding the port and closing another game will not help. `AccessDenied` is a port Windows has reserved for itself; `netsh interface ipv4 show excludedportrange protocol=udp` lists the reserved ranges. Set `UdpPort` to a port outside them and point the tracker at it.
- If the log says the port bound but no packet ever arrived, the tracker is sending somewhere else. Confirm the address and port in OpenTrack's output options, and that `UdpPort` in the config matches. On a phone, confirm the PC's LAN address and that your firewall allows the port inbound.
- Head tracking is paused outside gameplay: the pause menu, dialogs and popups, cutscenes, and any moment the game takes the view to point it somewhere. It is also paused while the game window is not focused, unless `PauseOnLostFocus` is `false`.

**Jittery or unstable tracking**

- Raise `LocalSmoothing` if the tracker sends to `127.0.0.1`, or `RemoteSmoothing` for any other address, including a tracker on this PC that sends to its network address.
- If the tracker is a phone app sending direct, route it through OpenTrack instead and turn its filters up. See [Phone App Setup](#phone-app-setup).

**Wrong rotation axis** (yaw feels wrong when looking up or down at extreme angles)

- Toggle between horizon-locked and view-local yaw with `Page Down` (or `Ctrl+Shift+H`). Horizon-locked, the default, is horizon-stable: turning your head rotates the view about the world's up axis whatever the camera is pitched at. View-local follows the camera's current up axis, so at a steep pitch it reads more like a roll.
- If the view sits off center or drifts away from where you are looking, center in the tracker: OpenTrack's Center bind, or your phone app's center button.

**Head tracking fades out when I raise the camera or a photo**

By design. While the instant camera, a held photo or a mounted camera is up to your eye, the frame on screen is the shot the game will take or the placement it will use. The mod fades head tracking out over a fraction of a second so what you frame is exactly what you get, and fades it back in when you lower it.

**The game window jumped to the middle of the screen**

By design. Running windowed, the mod centers the window on the work area of the monitor it is already on, the screen minus the taskbar. It decides once per launch and once per display mode, so drag the window where you like and it stays there until the window changes size or you switch between windowed and fullscreen. Fullscreen is never touched. The log line says what it did either way.

**Config changes are not taking effect**

The config is read when the game starts. Close the game, edit `BepInEx/config/com.cameraunlock.viewfinder.headtracking.cfg`, and launch it again.

**Leaning still puts the view through a wall**

- Check `CollisionEnabled` is `true`.
- Raise `CollisionRadius`. It has to be larger than the camera's near clip distance or the surface is culled and you see through it anyway; the log says so if the mod had to raise it for you.

## Updating

Download the new release and run `install.cmd` again. Your config is preserved.

## Uninstalling

Run `uninstall.cmd`. This removes the mod DLLs, and removes BepInEx (including the `dotnet` runtime folder it brings) only if the installer was what put it there. Use `uninstall.cmd /force` to remove BepInEx anyway.

## Building from Source

Prerequisites: [pixi](https://pixi.sh) and the .NET 8 SDK. No copy of the game is needed; every build reference comes from NuGet through `scripts/setup-libs.ps1`.

```powershell
git clone --recursive https://github.com/itsloopyo/viewfinder-headtracking.git
cd viewfinder-headtracking
pixi run package
```

`pixi run package` builds and produces the installer ZIP in `release/`. `pixi run test` runs the unit tests, and `pixi run install` deploys a Release build straight into your own game folder.

## Community & Support

- [Discord](https://discord.com/invite/dxyZdyFNT9) - setup help, bug reports, and new-release announcements
- [Lopari](https://lopari.app) - free Windows launcher with one-click install and launch of head-tracking mods
- [Headcam](https://headcam.app) - free app that turns your phone into a head tracker

## License

MIT License - see [LICENSE](LICENSE) for details.

## Credits

- Viewfinder by Sad Owl Studios, published by Thunderful Publishing.
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1)
- [HarmonyX](https://github.com/BepInEx/HarmonyX) (MIT)
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) (LGPL-3.0)
- [OpenTrack](https://github.com/opentrack/opentrack) (ISC)

## Disclaimer

This mod is not affiliated with, endorsed by, or supported by Sad Owl Studios or Thunderful Publishing. Use at your own risk.
