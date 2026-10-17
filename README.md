# Bedside Remote

Control a Windows desktop from an Android phone: a native universal remote, touchpad and keyboard, with a contextual YouTube/Netflix companion for Zen/Firefox. Local control uses authenticated, certificate-pinned TLS; no cloud relay or streaming credentials are needed.

The project is in **alpha**. Automated checks and fixture tests do not establish physical-device or live streaming compatibility. See [validation](docs/validation.md) for executed checks and outstanding real-device tests.

## Features

- D-pad, Windows Start, Escape, Unicode text, essential keys, touchpad movement/clicks/scroll, media and volume controls.
- LAN discovery, manual connection, explicitly approved pairing, encrypted credentials, reconnect and device revocation.
- Native Android YouTube sharing: play now, play next or append to the Windows-owned queue; reorder, remove, clear, previous/next and synchronized playback state.
- Optional extension with isolated YouTube/Netflix adapters, multiple player tabs, metadata, seek and currently available episode/intro/recap actions. Universal controls remain usable without the extension.
- Native dark/light Compose interface, one-hand controls, motion/accessibility support and restrained interaction feedback.
- Component CI and automatic SemVer releases with honest signing/physical-validation metadata and SHA-256 checksums.

Complementary work belongs in [the roadmap](docs/roadmap.md). See [the implementation plan](docs/implementation-plan.md), [architecture](docs/architecture.md), [security](docs/security.md) and [protocol](contracts/protocol.md).

## Requirements

- Android 8.0/API 26 or later and a Windows 10/11 x64 desktop on the same trusted private network.
- Firefox 142 or later, or a compatible Zen release, for the optional extension. Streaming sites still require their own valid access/subscription.
- Netflix DRM playback in Zen on Windows is **not guaranteed**: [Zen documents its Widevine limitation](https://docs.zen-browser.app/faq#why-cant-zen-browser-play-drm-protected-content). Firefox is the first supported DRM fallback. No protection bypass is implemented.

## Install

Download matching artifacts from [GitHub Releases](https://github.com/hugo2006alm/bedside-remote/releases). Check `release-manifest.json` and release notes before choosing a development build; verify file hashes against `SHA256SUMS.txt`.

1. **Windows:** run the x64 setup executable for your Windows user, then start Bedside Remote. The installer registers the Native Messaging host under your user account. Allow network access only on trusted private networks if Windows Firewall asks. The app normally needs no administrator privileges. System tray keeps the agent running; startup after login is optional.
2. **Android:** install the APK, allowing installation from the download source when Android prompts. A development APK uses a debug key; distribution updates require the same persistent signing key. If Android refuses a differently signed update, uninstalling loses saved phone pairings.
3. **Extension:** for a signed XPI, use Firefox/Zen's add-on manager, **Install Add-on From File**. For the development ZIP, extract it and temporarily load its manifest through `about:debugging#/runtime/this-firefox`; this is temporary developer loading, not signed installation. Reload existing player pages and approve the required site access. Inspect connection/target status from the extension popup.

Do not expose the agent's port to the Internet or enable router forwarding. If multicast discovery is blocked, use the computer's private IP and port manually.

## Pair and use

1. Open the Windows agent and start its temporary pairing window. Note its LAN address, port, TLS fingerprint and six-digit code.
2. On Android, choose the discovered computer or enter its address/port. Enter and verify the full SHA-256 certificate fingerprint from the Windows display before sending the code. Name the phone and request pairing.
3. Approve the named phone in the Windows prompt. Android saves the computer with its own encrypted credentials and reconnects automatically when selected.
4. Navigate with the remote, type with Android's native keyboard or open the touchpad. **Home** opens the Windows Start menu. Media shortcuts act on Windows; contextual player controls use the selected extension session.
5. In the official YouTube Android app, choose **Share → Bedside Remote**, then play now/next/add to queue. Choose a paired computer when necessary. The Windows agent owns queue order and persists it between sessions. A natural end advances only when supported; manual navigation and disconnects do not manufacture completion.
6. Select the active player/tab when multiple sessions exist. Netflix buttons appear only when the relevant controls are actually available. Audio/subtitles may open the website's menu; use universal navigation to make the selection.

Revoke a phone in the Windows device list to terminate access. Removing a saved computer on Android removes local credentials; revoke it on Windows as well. A reset server certificate requires verifying the new fingerprint before pairing again.

## Build and test

Use JDK 17 + Android SDK 36, .NET SDK 10 and Node.js 24. Gradle's wrapper verifies its distribution checksum. Installers use Inno Setup 6. Component READMEs provide details.

```powershell
# Android (from android/)
./gradlew spotlessCheck lint testDebugUnitTest assembleDebug

# Windows (from repository root)
dotnet restore windows/BedsideRemote.slnx
dotnet build windows/BedsideRemote.slnx -c Release
dotnet run --project windows/BedsideRemote.Tests -c Release

# Extension (from browser-extension/)
npm ci
npm run check
npm test
npm run build
npm run lint:extension
npm run package

# Release automation checks (from repository root)
node --test scripts/*.test.mjs
```

See [release configuration](docs/releasing.md) for signing secrets, automatic version calculation, prerelease policy and installation artifacts. Changes to `main` run the full pipeline; eligible Conventional Commits trigger publication only after all checks succeed. No open-source project license has been selected.

## Known limitations

- Windows input cannot control elevated applications, the secure desktop or lock screen without additional privileges; the agent deliberately runs as the ordinary interactive user.
- Browser autoplay and fullscreen require browser/user permission. Remote commands cannot manufacture a trusted user gesture; errors and unavailable capabilities are exposed. Enter fullscreen using local interaction or universal keyboard navigation when needed.
- Streaming websites can change their DOM. Fixture tests protect known adapter behavior, while real Netflix/YouTube playback, localization and DRM require testing on the target browser.
- DNS-SD depends on local network multicast. Manual connection remains available; public addresses are rejected.
- Missing Android, Windows or Mozilla signing credentials produces an explicitly incomplete development prerelease. An unsigned extension ZIP cannot be installed as a permanent signed add-on.
