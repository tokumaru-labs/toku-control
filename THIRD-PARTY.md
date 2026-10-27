# Licensing and third-party components

Copyright (c) 2026 Tokumaru Labs.

TOKU CONTROL's original source, documentation and original icon assets are
licensed under **GNU GPL version 3 only**; see [LICENSE](LICENSE). A narrow
[additional permission](LICENSE-NET-EXCEPTION) under GPLv3 section 7 permits
linking and distribution with Microsoft's unmodified .NET / Windows Desktop
runtime. It does not waive the GPL requirements for TOKU CONTROL itself.

The Windows portable download bundles .NET 10.0.13. These runtime components
retain their own licenses and are not relicensed as GPL by this project:

| Component / notice | License file in the portable download |
| --- | --- |
| Microsoft .NET Windows redistributables | `LICENSE.txt` |
| .NET third-party notices | `ThirdPartyNotices.txt` |
| .NET 10.0.13 runtime pack's third-party notices | `runtime-license/NETCORE-THIRD-PARTY-NOTICES.txt` |
| .NET MIT-licensed components | `runtime-license/DOTNET-MIT-LICENSE.txt` |
| Windows Desktop runtime pack's license | `runtime-license/WINDOWS-DESKTOP-LICENSE.txt` |
| WPF's D3DCompiler redistributable | `runtime-license/WINDOWS-SDK-LICENSE.html` |

Microsoft identifies the specific native Windows runtime files governed by
the .NET Library License or Windows SDK License in its
[Windows license information](https://github.com/dotnet/core/blob/main/license-information-windows.md).
Use and redistribution of those bundled components are subject to those
terms. Preserve all notices when redistributing the portable folder.

The source tree keeps the original .NET license and notices under
`runtime-license/`; the build copies them without editing their contents.
The updater uses **Velopack 1.2.161** (MIT) and its native Windows updater/stub.
The matching license is included as `runtime-license/VELOPACK-LICENSE.txt`.
Upstream build-tool vendor notices are preserved as
`runtime-license/VELOPACK-THIRD-PARTY-NOTICES.md`; not every build-tool vendor
binary described there is shipped in this Windows portable package.
Source: https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493

The application uses installed Windows fonts; it does not distribute font files.
The original icon and CRT shader sources are in `Assets/`. User-supplied design
reference images and private development captures are not part of this repository.

The IPolicyConfig declarations were independently written and cross-checked
against the [SoundSwitch interface declarations](https://github.com/Belphemur/SoundSwitch/blob/dev/SoundSwitch.Audio.Manager/Interop/Interface/Policy/IPolicyConfig.cs).
No SoundSwitch implementation, package or binary is included.

Matching application source, shader source and build scripts are available at
the release tag in [tokumaru-labs/toku-control](https://github.com/tokumaru-labs/toku-control).
