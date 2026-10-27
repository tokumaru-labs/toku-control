# Changelog

## 0.3.0 - 2026-10-27

- New version available notification with Yes / Update, Later, download progress and cancellation. Accepted updates are verified, applied and restarted with settings preserved.
- Startup checks for stable GitHub releases, a manual updates window and a persistent startup-check toggle. No download or update occurs without Yes; preview and verification modes do not check online.
- Portable ZIPs now include a stable launcher, `Update.exe` and `current/`, using pinned Velopack 1.2.161. Users of 0.2.3 or earlier must extract this new ZIP into a fresh folder once.
- Added consent/cancellation/failure, real local feed/checksum, update-dialog and packaging checks. Updated network/privacy and release documentation.

## 0.2.3 - 2026-10-27

- Quick Launch adds Command Prompt and the built-in Windows PowerShell, starting in the user's home folder.
- Right-click or Shift+F10 opens an Open / Run as administrator menu for those shells, Notepad, Task Manager and Task Scheduler. Administrator launches use the Windows UAC flow; cancelling is logged without retrying.
- All nine shortcuts fit the existing panel. Preview mode blocks normal and administrator launches.

## 0.2.2 - 2026-10-25

- Quick Launch now opens Task Manager, all classic Control Panel items, Task Scheduler and Windows Startup Apps settings.
- Compact shortcut rows keep all seven targets inside the existing panel; preview mode still disables launching.

## 0.2.1 - 2026-10-21

First public release of TOKU CONTROL.

- Native Windows console with a CRT-inspired theme and original icon.
- Playback volume/mute, explicit default-output switching and peak meters.
- LCD-style digital clock with silent timers.
- Three equal-width modules stacked beside the main console; grouped dragging.
- Windows system information and active monitor details.
- Live CRT controls, saved window layout and appearance settings.
- Optical-drive controls and fixed local application shortcuts.
- English/Japanese documentation, GPLv3 licensing and a self-contained Windows x64 download.

See README for compatibility limits and unsigned-binary information.
