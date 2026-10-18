# Validation

Automated checks, integration checks and physical validation are separate evidence. Passing a fixture test or build does not establish streaming compatibility.

## Automated checks

| Scope | Command / evidence | Result |
| --- | --- | --- |
| Release versioning, artifact safety and APK certificate verification | `node --test scripts/*.test.mjs` | 15 tests passed locally, including current/legacy apksigner output and rejection of unexpected signing identities. |
| GitHub Actions syntax/expressions | `actionlint` 1.7.12, verified official release checksum | Passed locally. |
| Extension protocol, URL and HTML/media fixtures | `npm run check`, `npm test` | 14 tests passed locally, including target synchronization, bounded snapshots and reconnect behavior. |
| Mozilla manifest/package validation and signing | `npm run build`, `npm run lint:extension`, `npm run package`, `npm run sign` | Passed without validator notices/warnings. Mozilla automated screening approved version 0.1.1 and returned its signed XPI; package identity/version and signature metadata verified. Live browser installation/playback remains pending. |
| Windows protocol, input validation, queue, pairing and framing | Windows core test executable | 89 checks passed locally, including a long-lived session exceeding 110,000 commands. |
| Windows real transport and native integration | Windows integration executable with published native host and offscreen preview | 42 checks passed locally; pinned TLS/WSS, revocation, named pipes, WinExe framing, queue/navigation, responsive input, busy rejection, ACK/admission race regression and target selection. |
| Android protocol, shared URLs, local endpoint restrictions and pinned transport | Gradle unit tests | All 8 unit/TLS/WSS loopback tests passed locally. Wrong pin prevents the pairing secret from reaching HTTP. |
| Android build, static checks and signing | `spotlessCheck lint assembleDebug assembleDebugAndroidTest assembleRelease`, `apksigner verify --print-certs` | Passed; lint has no errors. The minified release APK is signed with the persistent RSA-4096 identity and verified against `android/signing-certificate.sha256` in CI and after download. |
| Android native UI | Compose instrumentation tests, Actions API 35 Pixel 6 emulator and initial-screen capture | Both Compose tests passed. The exact signed distribution APK passed a clean install and launch smoke test; its process remained alive, its initial screen was captured and AndroidRuntime reported no crash. Captured APK hash matches the published artifact. |

The three components consume a shared corpus of official YouTube URLs and spoofed/malformed URLs in `contracts/youtube-url-cases.json`. Tests never inject real desktop input into the user's applications.

## Published signing and runtime evidence

[v0.1.1-alpha.1](https://github.com/hugo2006alm/bedside-remote/releases/tag/v0.1.1-alpha.1) was published by [a fully successful pipeline](https://github.com/hugo2006alm/bedside-remote/actions/runs/39112985683). Its manifest records Android and Mozilla signing as complete, Windows Authenticode as missing, and physical acceptance as incomplete. All eight published asset digests match the release checksum file; downloaded APK, XPI, source and metadata hashes were also checked. This remains a development prerelease.

The released Windows installer was also installed on the development PC. The running installed agent served protocol v1 over TLS pinned to its stored certificate and rejected a valid unauthenticated WebSocket handshake with HTTP 401. This test sent no desktop input.

Physical testing on 2026-10-18 used a Nothing A059P running Android 17 (API 37) and the exact signed v0.1.1-alpha.1 APK. Installation, manual LAN pairing with the agent certificate fingerprint and temporary code, explicit approval on Windows, secure connection, and automatic reconnection after force-stop/relaunch passed. Reinstalling the same signed APK preserved pairing and reconnected; this does not establish version-to-version upgrade behavior. Light/dark appearance was checked and restored to dark. Automatic LAN discovery did not find the PC in this network; its cause remains unresolved.

The signed extension was installed in Zen. Its native connection failed with `No such native application com.bedside.remote`; live playback and queue acceptance remain blocked on that investigation. Both Windows registry views contain the expected current-user host registration, the manifest is valid UTF-8 without a BOM, and a direct framed-message probe of the installed executable connected to the running agent and exited normally. These checks do not establish a working connection from Zen. Windows UI automation was unavailable because its runtime failed to initialize.

## Physical acceptance checklist

The observations above are partial physical evidence. The following checklist remains open until all relevant cases are executed and recorded with device/browser versions:

- Install/update APK and Windows setup; verify settings/pairings survive matching-key upgrades. Install the signed extension when Mozilla credentials are configured.
- Pair across the LAN with the displayed fingerprint, code and explicit Windows approval; reject incorrect fingerprint/code; verify reconnect, manual connection, discovery, revocation of an active client and firewall private-network behavior.
- Test D-pad, Unicode including Portuguese accents, gestures/scroll, native keyboard/IME behavior, media/volume and normal desktop applications. Confirm behavior at Windows integrity boundaries.
- Share official YouTube links from Android; play now/next/append; reorder/remove/clear; observe natural-end advancement once, manual video changes, multiple tabs, refresh/close, autoplay denial and network loss.
- On real Zen/Firefox, verify YouTube state/progress and contextual controls. On a supported DRM browser, verify Netflix playback, episode/intro/recap/audio/subtitle actions when available. Record unavailable controls and Zen DRM limitations; never bypass protections.
- Review Android dark/light, contrast, TalkBack labels, touch targets, font scaling, reduced motion, one-hand reach and performance/battery on typical hardware.

Do not set `PHYSICAL_VALIDATION_APPROVED=true` or promote to stable solely from CI. See [release configuration](releasing.md).
