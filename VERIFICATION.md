# Verification scope

This project has automated checks plus local Windows desktop checks. Passing
them is not a claim that every audio device, display layout or Windows build is supported.

`verify.ps1` runs seven reports:

1. Service checks: settings, bounded log, simulated audio, output-routing behavior and launcher requests/cancellation.
2. Native UI capture: real audio/system reads without changing OS audio or drive state.
3. Preview UI capture: operations against a simulated backend.
4. Module checks: visibility, closing/recreation, pinning and module buttons.
5. CRT checks: individual rendered pixel changes, RAW/restore/dim, state persistence.
6. Layout checks: vertical-stack geometry, no overlap, restore/persistence and grouped moves.
7. Update dialog: offer/Yes button layout, Later without downloading, explicit Yes wiring and persisted startup-check preference.

The service checks also exercise the production update session with controlled
backends: explicit consent, duplicate requests, cancellation, network failure,
settings-save failure, shutdown prevention and the save/apply/restart order.
A real Velopack local feed verifies newer-version discovery, package SHA-256,
corruption rejection and no downgrade. Invalid identity/path/hash/size metadata
is rejected. UI screenshots use a simulated future offer, not a published release.

For 0.3.0, `verify-update.ps1` also applied the candidate package over a disposable
0.2.99 portable fixture on the development Windows PC. The runner links the
production updater source and supplies a local feed only in the test project.
The native Update.exe waited for the runner to exit, replaced current/, and
restarted the real candidate app, which rendered its gallery and exited.
The installed manifest and binary version changed to 0.3.0; the startup-check
preference, CRT mode, bloom and text-size settings were retained outside current/.
No user's existing installation or settings are used by this test.

This is not a claim of validation for power loss during replacement, every
antivirus/file-lock combination or every Windows installation. Download failures
and corrupt packages are covered; native failure recovery for all environmental
conditions has not been exhaustively exercised.

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
