# Audit remediation — 2026-10-19

This records the fixes approved after the technical audit, plus the requested Windows opening/layout redesign. Implementation and initial workstation validation used `feat/audit-fixes-desktop-ui`. The local evidence below predates publication; CI and release links provide subsequent distribution evidence.

| Finding | Resolution | Evidence / limitation |
| --- | --- | --- |
| AUD-001: input survives agent stop | Shutdown rejects new requests/commands, cancels connection work and aborts sockets; repeated stop shares one task. | Real WSS test cancels blocked text and rejects further input within five seconds. |
| AUD-002: surviving browser cannot be selected | Show the session chooser before the missing-playback fallback, including one remaining unselected session. | Local Compose regression compiles; local device/emulator execution pending. Selection remains explicit. |
| AUD-003: saved IP cannot be changed | Devices exposes Edit address. Verify the retained certificate using `/health` before committing host/port, preserving credentials. | TLS loopback proves health has no bearer token and wrong pins send no HTTP request. DHCP recovery on a phone remains pending. |
| AUD-004: scoped IPv6 URL fails | Keep a URL-safe synthetic host with a direct scoped numeric DNS route and no proxy. | URL parser and DNS scope regression pass; real link-local interface routing remains pending. |
| AUD-005: drafts lost before acknowledgement | Keep text/link drafts while pending or rejected; clear/close only after a matching successful result. Never replay automatically. | Rejection callback unit test passes; Compose retained-draft test compiles. |
| AUD-006: partial native input leaves presses held | Track accepted unmatched presses and release the corresponding key/Unicode/mouse event. | Shift, Enter, Windows, Unicode, left/right mouse and zero-delivery checks pass using injected emitters. No physical input sent. |
| AUD-007: queue written on progress | Compare the serialized queue with the last durable snapshot; save only changes. | Twenty repeated native states preserve the queue file modification time. |
| AUD-008: unnamed fingerprint checkbox | One labelled toggleable row with checkbox role; child indicator is passive. | Local Compose labelled-target regression compiles; physical TalkBack execution pending. |
| AUD-009: overload appears revoked | Server closes with 1013. Android treats 1013 and legacy 1008 as transient; HTTP 401 means invalid/revoked authorization. | WSS overload/reconnect and Android close-code regressions pass. |
| AUD-010: stale sockets have no pong deadline | Ping every 20 seconds and require a pong within 15 seconds; connection cancellation stops input. | Configuration builds; simulated disconnect cancellation passes. Physical Wi-Fi blackhole timing remains pending. |
| AUD-011: overlapping saves resurrect credentials | Serialize fresh reads, transformation, durable save and publication in ComputerRepository. | Concurrent forget/pair and failed-save publication tests pass. |
| AUD-012: signing consumes version before other checks | AMO signing waits for Android, Windows and quality. Add validated manual fresh-version override for failures after external signing. | Version recovery regression and actionlint pass; no live AMO submission performed. See releasing.md. |
| AUD-013: node-forge development advisory | Still open: no patched version listed as of 2026-10-19. Retain the alert and documented limited reachability; do not apply npm’s incompatible web-ext downgrade. | See security.md and GHSA-86w9-cpqp-85rv. No claim of a dependency fix. |
| AUD-014: unsupported Netflix browser guidance | Point users to Zen/Firefox for YouTube and the approved Edge Store-app companion for Netflix. | Android and Windows builds pass. Actual Netflix playback remains unverified. |

Windows now uses the mobile charcoal/ivory/brass palette, bundled DM Sans/Manrope and rounded cards, with font licenses included. The header/footer stay reachable while cards scroll; optional settings and secondary addresses expand. Window bounds fit the monitor work area and DPI. Launching again activates/restores the existing instance rather than silently exiting. Startup runs once when the hidden window is reopened. Offscreen WPF rendering and work-area regression checks pass. The installed window initially reproduced a blank client area despite a complete accessibility tree; process-local software rendering restored visible content. The specific graphics-driver/composition cause is not isolated. PerMonitorV2 DPI awareness is explicit. Accessible phone names expose the phone label rather than the internal device record.

## Local validation

- Windows: zero build warnings/errors, formatting check, 89 core and 67 isolated integration checks; real pinned HTTPS/WSS, DPAPI and a simulated browser. No desktop input injected.
- Android: 16 unit/TLS/WSS tests pass; debug app and instrumentation APKs compile. Lint has no errors (existing informational/upgrade warnings remain). Five Compose tests are compiled but not executed locally: no connected device/emulator.
- Release scripts: 20 tests; GitHub Actions actionlint passes.
- Windows installer and Android minified release build are checked separately from streaming/device acceptance. No Windows signing, AMO resubmission or physical streaming result is inferred from these checks.

## Installed local build

The unsigned Windows `0.2.1-alpha.1.local` installer was applied over the existing installation. Hashes of settings, queue, DPAPI identity and device files were unchanged during installation. The real window renders at 960 × 1050 physical pixels on the current 144-DPI display and fits its work area. Reopening restores both minimized and tray-hidden states; readiness remains intact. UI Automation confirms the pairing dialog opens/closes and phone names omit internal record metadata. The window is left open. This is local workstation evidence, not clean-machine or streaming acceptance.

The Android minified APK `artifacts/bedside-remote-android-v0.2.1-alpha.1.local.apk` is version code 17 (the last successful main release workflow is run 16). Its APK signature verifies against the existing pinned release certificate. No signing key was changed. No Android device is connected, so this build is not installed and its five Compose tests remain pending execution. Both packages are local candidates; no GitHub release was published.
