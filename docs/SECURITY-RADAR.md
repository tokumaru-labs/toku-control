# Security Radar / Attack Map

Open **security radar** in the left navigation, then **READ / REFRESH**. Opening
the window does not start a scan. Seven technique families map to nine Windows
observation sources. **ATTACK MAP** opens a grouped overview of technique-to-source
relationships; it is also the initial view. Click a node or left-hand card for
evidence, then use **ATTACK MAP** to return to the overview. The diagram is not an
observed intrusion path. Selecting a view never starts collection. Evidence includes
capture time, scope, failure reason and interpretation limits. **SOURCES** shows
the same evidence by collector.
The radar requires no internet, extra packages, paid service or administrator
prompt and never changes Windows security settings.

## Coverage

| Stage | Source | Observed scope |
| --- | --- | --- |
| Protection | Defender | Service, antivirus, real-time, behavior and tamper-protection flags; mode and signature timestamp. False values request review; absent properties remain unknown. Other AV/passive mode require context. |
| Protection | Firewall | Domain, Private and Public profiles in ActiveStore, including inactive profiles. Rules, filtering effectiveness and external reachability are not tested. |
| Persistence | Run keys | Current-user/machine Run and RunOnce, both registry views. Values are not expanded or executed. Other users and autorun locations are outside scope. |
| Persistence | Startup folders | Current-user/common local fixed-drive folders; SHA-256 of file content, including shortcuts. Targets are not resolved. Redirected folders/reparse points and files over 8 MiB produce partial status. |
| Persistence | Services | Win32_Service executable path, start mode, account and delayed start. Transient state, binary contents, service DLLs and drivers are excluded. |
| Persistence | Scheduled tasks | Definitions visible to this user, action text and definition SHA-256. Tasks hidden by permissions may not be visible. Running/ready transitions are excluded. |
| Authentication | Security log | Latest 500 events 4624, 4625, 1102 within a bounded 24-hour window. Time, logon type, account, IP, status/substatus when present. Audit policy and retention are not verified. |
| Network | TCP | Current IPv4/IPv6 endpoints, state, owning PID and process name. Process exit/reuse may race the lookup. |
| Network | UDP | Local bound endpoints and owning process; remote peers and transmitted data cannot be inferred. |

All collectors are fixed embedded code. Values and task actions are data, never
commands. No arbitrary command interface, port scan, traffic generation, capture,
DNS/reputation/geolocation lookup, signature-validation network calls, tool
downloads, malware samples, blocking or remediation are included. Normal OS
logging may record the read queries. The separate existing application updater
retains its existing consent/settings behavior; the radar itself never connects.

## Reference, failures and false positives

Initial inventories show **NO REFERENCE**, not suspicious changes. Review the four
persistence inventories and use **SET REFERENCE** to fix them in memory for this
app session. This is a comparison point, **not a declaration that entries are
trusted**. Later reads show additions, removals and exact value changes. Legitimate
installs/updates also produce these. Re-setting explicitly replaces the reference;
closing the radar retains it, closing the app discards it. Normal UI use does not
save or upload raw snapshots, IPs, accounts, commands or reference files.

Unavailable, cancelled or truncated sources are **UNKNOWN**. Partial rows can be
inspected but cannot become a reference or produce deletion claims. Any failed
persistence source blocks setting the four-source reference. Refresh clears the
current observations first so stale success is not presented as fresh evidence.
Each source has its own time: this is not an atomic snapshot or continuous monitor.

Each probe has a 20-second deadline and cancellation. Limits: 2,000 inventory rows,
500 authentication events and 1,000 network rows per source, a combined text budget
and bounded stdout/stderr. Limits produce partial status. Access denial never
causes elevation or audit-policy changes. Zero events does not mean no attacks.
Connections, listening ports, failed logons and startup changes alone do not prove
malware, intrusion or exfiltration. Observed enabled controls do not prove safety.

## Primary-source reconciliation

Reviewed 2026-11-01. The offensive glossary selected themes only; its prose,
statistics, claimed coverage and tool list are not redistributed or used as rules.
Supplied text SHA-256:
`04164d730d85586cd90e662c0e7148f0b946e1056dd4dd223fe14683399bb9e1`.
The visible export starts with initial-access content and advertises a much larger
dictionary. This work does not claim all 500 advertised checks were audited.

| Theme | Primary references | Mapping boundary |
| --- | --- | --- |
| Protection impairment | [Defender status](https://learn.microsoft.com/en-us/powershell/module/defender/get-mpcomputerstatus), [MITRE T1685](https://attack.mitre.org/techniques/T1685/) | Status inspection, not bypass detection. Current T1685 supersedes old T1562.001. |
| Firewall impairment | [Get-NetFirewallProfile](https://learn.microsoft.com/en-us/powershell/module/netsecurity/get-netfirewallprofile), [T1686](https://attack.mitre.org/techniques/T1686/) | Profile flags only; current family replaces old T1562.004. |
| Autostart | [T1547.001](https://attack.mitre.org/techniques/T1547/001/), [Run/RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys) | Selected keys and Startup files only. |
| Services/tasks | [Win32_Service](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-service), [Get-ScheduledTask](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/get-scheduledtask), [T1543.003](https://attack.mitre.org/techniques/T1543/003/), [T1053.005](https://attack.mitre.org/techniques/T1053/005/) | Configuration changes, not attribution of abuse. |
| Authentication | [4625](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4625), [4624](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4624), [1102](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-1102), [Get-WinEvent](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-winevent), [T1110](https://attack.mitre.org/techniques/T1110/) | Failure context, not a brute-force assertion. Log clearing also requests review. |
| Network/C2 | [Get-NetTCPConnection](https://learn.microsoft.com/en-us/powershell/module/nettcpip/get-nettcpconnection), [Get-NetUDPEndpoint](https://learn.microsoft.com/en-us/powershell/module/nettcpip/get-netudpendpoint), [T1071](https://attack.mitre.org/techniques/T1071/) | Endpoints, not application protocol/C2 detection. |

In-app descriptions are original explanations. Primary URLs are selectable text;
opening the map never automatically browses those URLs.

## FLARE Learning Hub

Inspected [README](https://github.com/mandiant/flare-learning-hub/blob/ebd4e3a7dd71c0082db2748aba13e300447cd2e0/README.md),
[LICENSE](https://github.com/mandiant/flare-learning-hub/blob/ebd4e3a7dd71c0082db2748aba13e300447cd2e0/LICENSE)
and the complete tree at `ebd4e3a7dd71c0082db2748aba13e300447cd2e0`.
The repository uses Apache-2.0. Modules link to Google Docs; `macc/` and `ttd/`
contain exercise ZIPs, an assembly PDF, debugging JavaScript and a raw binary
payload. The README recommends isolated VMs; exercises can exhibit malware-like
behavior. This is training material, not a ready-made endpoint detection engine.

This change redistributes **none** of its text, samples, scripts, PDFs or binaries
and does not install FLARE-VM. The [hub](https://github.com/mandiant/flare-learning-hub)
is an optional learning reference. Future redistribution needs an artifact-specific
terms check, applicable licenses/notices and modification notices. The repository
license is not assumed to cover externally hosted Google Docs automatically.

## Existing code and Agent Guard

TOKU CONTROL was inspected at `aa6fe6e36d9a182a0a086caeb2cc828815078d03`
(v0.3.0): audio/routing, launcher, CRT, module layouts, system info and updater.
It had no radar collector or GitHub Actions Windows workflow. Existing behaviors
and tests remain; the navigation scrolls to accommodate the added entry.

Agent Guard is a separate ZIP delivery, not an established GitHub dependency.
The available v0.1 ZIP was inspected read-only, SHA-256:
`5450f0f17567c3a5f21921136e679291d4e17ccdd3625276c23c9e58ef5804f9`.
It hashes AI instruction/configuration files, records explicit trust and gates
launching. It is not a protection/event/network collector, and is not executed,
modified or bundled here. The v0.2 source ZIP was then retrieved from its original
delivery and inspected (SHA-256
`38ee17bba193b8f548acc2d11795dfabdfd7b18f1979de773ad9d3067125f659`).
Its `Collect-Network.ps1` already uses Get-NetTCPConnection plus process metadata,
bounded JSON and no DNS/disk logs. That read-only collection approach is retained
in the native adapter, with UDP/listening sockets, failure states and per-source
bounds added for this feature. Its Python/browser server, optional country data,
maps and separate confirmed firewall-control script are not bundled or invoked.
Agent Guard's instruction-file trust/launch workflow remains unchanged. This is
not a claim that the separate v0.2 application's Windows blocking was validated.

## Validation and recovery

- `verify.ps1`: existing native/simulated suites plus radar WPF fixtures and
  `tools/Test-RadarProbes.ps1` helper regressions (redirect, access failure, bounds).
- `--radar-self-test --result <path>`: deterministic radar, routing, launcher and
  updater checks suitable for CI without live-device assumptions.
- `--radar-live-check --result <path>`: adds real collection, reporting only
  counts/status metadata, never raw observations.
- `--preview --data-dir <temporary-path> --radar-check-dir <path>`: verifies
  navigation, singleton, reference operation, resizing and reopening with clearly
  labeled fixtures. Synthetic actions do not establish physical hit testing.
- Windows CI builds self-contained output and runs deterministic/WPF fixture
  checks with repository read permission. Only fixture reports/screenshots upload.

Rollback: close the candidate and run the prior installed app, or revert the
feature commit before merging. No host security settings, audit policy, services,
tasks, autoruns or firewall rules require restoration. Merge/release remains a
separate owner decision.
