# Changelog

## [0.0.0] - 2026-09-14

### Added
- Initial release.
- Added head tracking for Viewfinder: yaw, pitch and roll, plus positional lean, peek and duck, driven by any OpenTrack compatible tracker on UDP port 4242.
- Added decoupled look and aim: head tracking moves the view while the mouse or controller keeps aiming.
- Moved the game's own reticle onto the point you are really aiming at while head tracking is active.
- Added a head tracking on/off toggle on `End` or `Ctrl+Shift+Y`.
- Added a three-position tracking mode cycle (full, rotation only, position only) on `Page Up` or `Ctrl+Shift+G`.
- Added horizon-locked and view-local yaw modes on `Page Down` or `Ctrl+Shift+H`.
- Added a lean clamp that sweeps the lean against the level, so leaning into a wall cannot put the view inside it.
- Added field-of-view scaling, so head tracking moves the view by the same amount on screen whatever the game does with its field of view.
- Added window centring, so the game window is centred on its monitor when Viewfinder runs windowed.
- Added separate `LocalSmoothing` and `RemoteSmoothing` settings for trackers on this machine and on other devices.
- Paused head tracking in menus, cutscenes and popups, and while the game window is not focused.
