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

## Validation limits

Fixtures cover available controls and observable state changes. Packaging checks verify identity/permissions; native integration tests cover exact-origin admission and framed stdio. Real Store-app playback, profile/extension loading, metadata, play/pause/seek, intro/recap/episode controls and audio/subtitle access still require the user's session. Builds do not establish DRM, account or autoplay compatibility. Universal controls remain available. Never bypass protections or enable debugging in the user's authenticated profile.
