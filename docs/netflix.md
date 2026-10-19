# Netflix Store app and Edge companion

Zen shares Firefox source but does not inherit its Widevine license. [Zen documents its Windows/macOS DRM limitation](https://docs.zen-browser.app/faq#why-cant-zen-browser-play-drm-protected-content); Bedside Remote cannot grant that license.

The installed Netflix package `4DF9E0F8.Netflix` version 7.0.8.0 declares `Microsoft.MicrosoftEdge.Stable` as its host and a PWA application in its package manifest. [Netflix confirms its Windows app requires Edge](https://help.netflix.com/en/node/23931). [Microsoft documents extensions in PWA windows](https://learn.microsoft.com/microsoft-edge/progressive-web-apps/ux). Integration uses Edge; it does not modify a Netflix executable.

## Setup

1. Install the matching Windows agent update, which registers separate Mozilla and Edge Native Messaging manifests.
2. Extract `bedside-remote-edge-…-development.zip` to a persistent directory. In the Edge profile hosting Netflix (the inspected package declares `Default`), open `edge://extensions`, enable Developer mode and choose **Load unpacked** from the directory containing `manifest.json`. Do not load the Zen XPI in Edge.
3. Verify extension ID `lggdbmfkngfahhnmmibhmefgfgkgngkf`. Its committed public key fixes the ID; it is not a private signing credential. Edge 148+ is required. Permissions remain limited to the existing official player hosts, Native Messaging and reconnect alarms.
4. The toolbar popup should say **Windows agent connected**. If registration is missing, run `./scripts/repair-browser-integration.ps1 -Browser Edge` in your normal PowerShell after installing the updated agent.
5. Close/reopen the Netflix Store app to load the extension in a fresh document. Sign in yourself if required and start a title. Select Netflix on Android if several players exist. Bedside Remote never requests Netflix credentials.

The Edge MV3 service worker shares the public-DOM/HTML-media adapter and uses callback responses for asynchronous messages. The host accepts only the fixed Edge origin and an optional numeric parent handle. Unpacked installation is developer loading, not signed Edge Add-ons distribution.

## Confirmed connection repair

On 2026-10-19 the user initially saw a brief connected indication followed by `Specified native messaging host not found`. Registration checks and rewrites in the tooling context did not establish a lasting browser connection. Running `scripts/repair-browser-integration.ps1 -Browser Edge` from the user's ordinary, non-administrator PowerShell restored the connection, as confirmed by the user. Process inspection also found the installed native executable launched with the exact allowed Edge origin. The reason the execution contexts observed different behavior remains unisolated. This confirms the browser-to-Windows connection, not Netflix playback or remote controls.

## Validation limits

The user subsequently confirmed Home preview metadata and pause control, but entering an episode removed the player from both extension and Android while the actual Edge player continued. Route recovery now requests a new snapshot, verifies the current document instance and retains the selected tab; the Netflix adapter prefers the watch player's video over a retained preview. Initial local tests still used an older extension loaded from Documents while updates had been copied elsewhere. After updating that actual loaded directory, the user confirmed that the episode player and metadata appeared in both extension and Android. This validates that partial path, not every control.

The same observation identified concatenated series/episode text and an audio/subtitle menu that opened but returned a false failure and would not close. The user confirmed the title spacing correction. Shared-menu fixtures and a hover-controller exit fallback pass, but the real menu still reports failure; its identifiers have not been confirmed. Failed commands now include only bounded public control identifiers for future diagnosis. Android exposes one combined button in the updated APK. The user deferred further Edge/Netflix physical testing on 2026-10-19; this remaining menu issue does not block the separately requested remote/trackpad/pairing/launcher release.

The Android D-pad offers browser focus navigation and Windows keyboard mode when a browser session is selected. Browser arrows select visible controls/cards by position; OK activates the focused item. The focus outline identifies the selection. This uses public DOM focus/click behavior; unavailable targets and gesture restrictions remain explicit errors.

Fixtures cover available controls and observable state changes. Packaging checks verify identity/permissions; native integration tests cover exact-origin admission and framed stdio. Real Store-app playback, profile/extension loading, metadata, play/pause/seek, intro/recap/episode controls and audio/subtitle access still require the user's session. Builds do not establish DRM, account or autoplay compatibility. Universal controls remain available. Never bypass protections or enable debugging in the user's authenticated profile.
