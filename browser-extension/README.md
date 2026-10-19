# Bedside Remote browser extension

Firefox/Zen desktop companion for the Windows agent. Protocol and Native Messaging identities are defined in [`../contracts/protocol.md`](../contracts/protocol.md). YouTube and Netflix use separate adapters over public DOM and standard HTML media APIs. No streaming credentials, cookies, private player APIs, DRM circumvention, remote code, or local WebSocket bridge.

## Build and validate

Use Node.js 24 and npm. From `browser-extension/`:

```powershell
npm ci
npm run check
npm test
npm run build
npm run lint:extension
npm run package
```

`check` runs strict TypeScript checking and source safety/style checks. `test` uses local HTML/media fixtures and the shared YouTube URL corpus; fixtures do not prove compatibility with live streaming services. `lint:extension` uses Mozilla's `web-ext` validator with warnings treated as errors. `package` produces an **unsigned development ZIP** and a separate AMO reviewer source ZIP under `artifacts/`. The reviewer archive preserves `browser-extension/` and `contracts/`; extract it and run the commands above from `browser-extension/` to reproduce the build. Dependencies are pinned in `package-lock.json`; esbuild bundles readable local TypeScript into three scripts, without minification or remote dependencies.

`APP_VERSION` overrides the shared `major.minor.patch` version during build. A SemVer prerelease suffix is removed for the Firefox manifest. Prerelease and stable builds with the same numeric version therefore cannot both be published as different AMO versions; assign a new numeric version before signing another submission.

## Install and connect

For the Netflix Store app, use the separate Edge package and [PWA setup instructions](../docs/netflix.md). `npm run build:edge` produces `dist-edge/` and `artifacts/bedside-remote-edge-development.zip`, with a fixed public-key ID, MV3 service worker and asynchronous response bridging. It adds no dependencies. Unpacked installation requires Edge Developer mode and is not signed Edge Add-ons distribution. Install the matching Windows agent for its separate exact-origin native registration. Real Netflix playback remains pending until tested in the user's session.

1. Build, open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on**, and select `dist/manifest.json`. Development installs are temporary and must be reloaded after restarting Firefox. Reload already open player pages after first installation.
2. Install/start the Windows agent and register its current-user Native Messaging host, `com.bedside.remote`, with the allowed extension ID `bedside-remote@hugo2006alm`.
3. Approve site access when Firefox requests it. Open a YouTube/Netflix video and use the popup to select the remote target and inspect native connection/player status. The Windows agent selects corresponding sessions for the phone.

Firefox 142 or later is required. This baseline consistently supports the built-in collection consent declaration in Mozilla's validator. The extension declares browsing activity, website content and website activity because titles, YouTube video IDs/thumbnail links and playback state leave the browser for the local Windows agent and paired phones. No analytics or cloud service is used. Only the exact official YouTube and Netflix HTTPS hosts in `static/manifest.json` are accessible. `youtu.be` is accepted for URL normalization, but no content script is injected there because it redirects to YouTube. No broad `tabs` permission is needed. The only API permissions are `nativeMessaging` and `alarms` (reconnect after event-page suspension).

The Firefox Manifest V3 background script uses a Native Messaging port and restores state by requesting snapshots from existing content scripts. After disconnect it retries with a bounded 1–30 second backoff; a 30-second alarm restores the event page if it was suspended. Commands are serialized; old commands are not replayed on reconnect. The browser handles native framing; application messages are limited to 64 KiB UTF-8 and 32 players. Snapshots prioritize the selected target and include only the players that fit both limits. The popup retains all compatible tabs so selecting an omitted tab brings it into the next native snapshot.

The popup includes the browser's disconnect reason when available. For `No such native application com.bedside.remote`, verify the Windows installer's current-user registration at `HKCU\Software\Mozilla\NativeMessagingHosts\com.bedside.remote`, the referenced JSON file, and the installed add-on ID (`bedside-remote@hugo2006alm`). Use the [registration repair instructions](../windows/README.md#pairing-and-browser-integration) from the user's ordinary Windows terminal; a successful check in a separate tooling context is not proof that the normal browser sees the same registration. A manifest validation or extension allowlist failure can also prevent host discovery; the browser console may provide a more specific error. Keep the allowlist restricted to this add-on.

The popup shows **Connecting** until Windows confirms that the native executable reached the interactive agent. A Port object alone never shows **Connected**. After five seconds without confirmation it asks you to start/update Windows and retry; a valid command from an older host also confirms the connection. Retry replaces the previous connection and ignores its late callbacks.

The toolbar and popup share the [application mark](../assets/brand/README.md), charcoal/ivory/brass palette and bundled DM Sans/Manrope fonts. Both browser packages include font license notices. The reviewer source archive also includes those Android-hosted font sources and notices for a standalone rebuild.

## Player behavior and practical limits

- Same-document route changes request a fresh snapshot and keep the selected tab. New instances or mismatched sender/current URLs must prove their identity through the active top document; rejected snapshots retry without requiring a media value change. Netflix watch video/title take precedence over retained Home previews.
- Browser D-pad commands select visible enabled DOM controls by spatial position, keep visible-dialog focus contained and activate the focused element with OK. A brass outline marks the selection. Windows mode retains ordinary keyboard arrows; no failed browser action silently becomes scrolling or global keyboard input.

- Remote **Play** calls standard `video.play()` and reports autoplay denial. Opening a YouTube URL waits for a usable player and attempts playback; consent/interstitial pages and autoplay restrictions produce explicit errors. The host acknowledgement is not proof that streaming playback succeeded.
- **Fullscreen** cannot obtain transient user activation from a remote command. The adapter advertises it only while exiting an existing fullscreen is possible. Enter fullscreen with a local browser click or use the universal remote keyboard fallback. No synthetic gesture bypass is attempted.
- **Seek** requires a finite duration and an actual seekable range. YouTube subtitles toggle only when their public button is present and confirms a state change.
- Netflix skip-intro, skip-recap and next-episode are offered only when visible, enabled public controls exist. Audio/subtitles toggle the same public player menu; Android presents one combined button. Confirmation observes that menu's visibility or expanded state, including closure, and supports hover-controller leave events without changing menu styles. An unrelated dialog cannot confirm success. Titles separate nested text fragments so series, episode number and episode name remain readable. DOM changes and localization can remove actions; adapters must be updated using live evidence, without pretending unsupported controls exist.
- The selected target persists across background tab progress updates. Browser tab activation or an explicit native/popup selection changes it. Android session selection is synchronized through the native-only `selectTab` command (`tabId` required, no URL or position); this updates the popup target and publishes state without activating the browser tab or invoking a player action. Tab removal/navigation clears stale state. Only an actual trusted media `ended` event latches `ended`; observed YouTube advertising, reload, removal and manual URL navigation do not manufacture natural completion. The Windows agent remains responsible for active-session matching and exactly-once queue advancement.
- A single top-level video player per tab is supported. No frame permissions or private page-world bridge is used. Player metadata and progress are sampled once per second; important media events and target changes publish immediately. Browser APIs and native host connectivity require real-browser integration validation.
- Netflix officially documents supported browsers; this repository does **not** assert that Netflix DRM playback works in Zen on Windows. Verify actual Firefox/Zen versions, DRM, account, autoplay and contextual controls manually. The universal remote works independently of this extension.

## Signing and release

Normal Firefox installations require an AMO-signed XPI for permanent installation. With AMO API credentials available as `WEB_EXT_API_KEY` and `WEB_EXT_API_SECRET`:

```powershell
npm run sign
```

This submits the built add-on plus reproducible reviewer source for **unlisted** signing. Credentials belong in local environment variables or repository secrets, never files or logs. Signing is an external service operation and is not part of local validation. An unsigned ZIP must never be presented as a signed installable release.

Development dependency review (2026-10-18): `npm audit` reports an upstream high-severity signature-verification advisory in `node-forge` 1.4.0 through `web-ext`'s unused Android ADB tooling (`@devicefarmer/adbkit`). The registry currently has no patched `node-forge` version; the suggested old `web-ext` downgrade is unsuitable. These development packages are not included in the built extension. Recheck and upgrade when an upstream fix exists; `npm audit --omit=dev` checks the shipped npm dependency surface separately.

Design references: [Mozilla MV3 migration](https://extensionworkshop.com/documentation/develop/manifest-v3-migration-guide/), [Native Messaging](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging), [Firefox collection consent](https://extensionworkshop.com/documentation/develop/firefox-builtin-data-consent/).
