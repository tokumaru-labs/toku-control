# Verification scope

This project has automated checks plus local Windows desktop checks. Passing
them is not a claim that every audio device, display layout or Windows build is supported.

`verify.ps1` runs six reports:

1. Service checks: settings, bounded log, simulated audio, output-routing behavior and launcher requests/cancellation.
2. Native UI capture: real audio/system reads without changing OS audio or drive state.
3. Preview UI capture: operations against a simulated backend.
4. Module checks: visibility, closing/recreation, pinning and module buttons.
5. CRT checks: individual rendered pixel changes, RAW/restore/dim, state persistence.
6. Layout checks: vertical-stack geometry, no overlap, restore/persistence and grouped moves.

Native and preview UI captures also check that all nine Quick Launch buttons
fit without overlap in the home/launcher views, scaled rendering and the small window.
Button/menu wiring is exercised with a recording process starter: normal clicks,
context-menu Open, administrator requests, preview blocking and UAC cancellation.
Service checks also cover fixed Windows paths, shell working directories,
unsupported elevation, access-denied failures and no automatic retries.
These automated checks do not launch programs or approve UAC; native shell
launches and actual UAC consent/credentials require a separate desktop check.

For 0.2.3, both shell executable requests were also started on the development PC
with temporary commands that recorded their working directory and exited. Both
reported the user's home folder. The context menus were rendered and inspected.
Actual UAC consent/credential entry and an elevated shell token were not exercised;
administrator request construction and cancellation were checked with simulated starters.

For 0.2.2, actual Quick Launch button clicks opened Task Manager, the classic
All Control Panel Items view, Task Scheduler, and Settings > Apps > Startup on
the development PC. No task or startup configuration was changed during this check.

All require a Windows interactive desktop. The rendering tests are not intended
for a headless Linux runner. `--preview` does not make the normal system panel's
values fictional; only the explicit gallery capture replaces displayed values with demo data.

The development build was also checked with actual mouse dragging in both
directions: visible modules followed the main window by the same displacement.
Actual default-output switching and rollback were checked on the development
PC; optical eject worked there, while tray close was unsupported on that drive.

Not verified exhaustively: hardware hot-plug during operations, optical-drive
access-denied/timeouts under real hardware faults, mixed-DPI Windows monitor
transitions, snap/maximize dragging, long-running sessions and other machines.

Raw verification reports and captures are generated under `artifacts/verification`
and ignored by Git because they can contain machine/device information.
