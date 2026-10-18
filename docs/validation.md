# Validation

Automated checks, integration checks and physical validation are separate evidence. Passing a fixture test or build does not establish streaming compatibility.

## Automated checks

| Scope | Command / evidence | Result |
| --- | --- | --- |
| Release versioning and artifact safety | `node --test scripts/*.test.mjs` | 10 tests passed locally. |
| GitHub Actions syntax/expressions | `actionlint` 1.7.12, verified official release checksum | Passed locally. |
| Extension protocol, URL and HTML/media fixtures | `npm run check`, `npm test` | 14 tests passed locally, including target synchronization, bounded snapshots and reconnect behavior. |
| Mozilla manifest/package validation | `npm run build`, `npm run lint:extension`, `npm run package` | Passed locally without validator notices/warnings. No AMO signing performed. |
| Windows protocol, input validation, queue, pairing and framing | Windows core test executable | 89 checks passed locally, including a long-lived session exceeding 110,000 commands. |
| Windows real transport and native integration | Windows integration executable with published native host and offscreen preview | 42 checks passed locally; pinned TLS/WSS, revocation, named pipes, WinExe framing, queue/navigation, responsive input, busy rejection, ACK/admission race regression and target selection. |
| Android protocol, shared URLs, local endpoint restrictions and pinned transport | Gradle unit tests | All 8 unit/TLS/WSS loopback tests passed locally. Wrong pin prevents the pairing secret from reaching HTTP. |
| Android build and static checks | `spotlessCheck lint assembleDebug assembleDebugAndroidTest assembleRelease` | Passed locally; lint has no errors. Debug APK signature verified. Minified release build is unsigned without distribution credentials. |
| Android native UI | Compose instrumentation tests, Actions API 35 Pixel 6 emulator and initial-screen capture | Both Compose tests passed on the Actions emulator. Standalone screenshot capture reinstalls the APK after instrumentation teardown. See Actions for current execution evidence. |

The three components consume a shared corpus of official YouTube URLs and spoofed/malformed URLs in `contracts/youtube-url-cases.json`. Tests never inject real desktop input into the user's applications.

## Physical acceptance checklist

No Android device was connected during initial implementation. The following remain pending until actually executed and recorded with device/browser versions:

- Install/update APK and Windows setup; verify settings/pairings survive matching-key upgrades. Install the signed extension when Mozilla credentials are configured.
- Pair across the LAN with the displayed fingerprint, code and explicit Windows approval; reject incorrect fingerprint/code; verify reconnect, manual connection, discovery, revocation of an active client and firewall private-network behavior.
- Test D-pad, Unicode including Portuguese accents, gestures/scroll, native keyboard/IME behavior, media/volume and normal desktop applications. Confirm behavior at Windows integrity boundaries.
- Share official YouTube links from Android; play now/next/append; reorder/remove/clear; observe natural-end advancement once, manual video changes, multiple tabs, refresh/close, autoplay denial and network loss.
- On real Zen/Firefox, verify YouTube state/progress and contextual controls. On a supported DRM browser, verify Netflix playback, episode/intro/recap/audio/subtitle actions when available. Record unavailable controls and Zen DRM limitations; never bypass protections.
- Review Android dark/light, contrast, TalkBack labels, touch targets, font scaling, reduced motion, one-hand reach and performance/battery on typical hardware.

Do not set `PHYSICAL_VALIDATION_APPROVED=true` or promote to stable solely from CI. See [release configuration](releasing.md).
