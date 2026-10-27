# Building and publishing updates

The update-enabled portable layout starts with 0.3.0. Earlier flat ZIP releases
need a one-time manual migration into a fresh folder. Settings stay in the
existing `%LOCALAPPDATA%\TokumaruLabs\TokuControl` directory outside the package.

Use Windows, the SDK from `global.json`, and Velopack CLI **1.2.161**, matching
the `PackageReference`. The local tool version is pinned in `.config/dotnet-tools.json`.
Restore it with `dotnet tool restore`. `package.ps1` accepts explicit `-Dotnet`
and `-Vpk` executable paths; when using a private SDK installation, set
`DOTNET_ROOT` to that SDK directory so the vpk executable finds its runtime.

1. Increment `Version` in TokuControl.csproj and update changelog/start guide.
2. Build and run `verify.ps1`. Keep preview/testing state in separate folders.
3. Generate current demo screenshots, then run `package.ps1` against fresh output
   directories. Outputs go to `artifacts/release-v<version>`.
4. Inspect the portable ZIP. Its root has `.portable`, `TokuControl.exe` (the
   stable launcher), `Update.exe`, and `current/` (the application and runtime).
5. Test an update from an earlier disposable portable package to the candidate
   package, including restart and persistence. Test cancellation/corruption
   without modifying an installed user's app or settings.
   Run `verify-update.ps1 -OldZip <older-portable.zip> -CandidateFeed
   <release-directory> -Destination <fresh-test-directory> -Dotnet <dotnet>`.
   Before the first updater release, use a locally built 0.2.99 fixture with the
   same package ID; never publish that fixture. The test project links the
   production update code and supplies the local source only to its own runner.
6. Publish the following together on a **stable GitHub release** in
   `tokumaru-labs/toku-control`: the user ZIP, `TokuControl-win-Portable.zip`,
   `TokuControl-<version>-full.nupkg`, `releases.win.json`, `RELEASES`, and
   `SHA256SUMS.txt`. The generated `assets.win.json` is build metadata and can
   remain local. Do not publish the feed before the matching package is uploaded.
7. Verify the public feed and package checksum, then update website download
   links/version/size. Verify actual Pages deployment separately.

The client uses the fixed public GitHub source, no token, stable `win` channel,
no downgrades, and full packages. It requires matching package identity and
SHA-256 metadata. Velopack verifies the download before applying it. Pending
downloads are **not** automatically applied at startup: each update needs Yes.

The app saves settings before asking the updater to wait for a graceful exit.
It refuses to restart while an audio-output or optical-drive operation is in
flight. Preview and capture modes do not check for updates. Diagnostic logs stay
local. TLS and feed hashes protect transport/integrity; these unsigned releases
do not claim publisher code-signature verification.
