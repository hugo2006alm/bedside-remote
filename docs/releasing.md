# Builds and releases

## Automatic flow

Each push to main runs version calculation and calls Android, Windows and extension CI. Android also runs Compose tests on an API 35 emulator, then installs the exact distribution APK for a launch/crash smoke test and captures its real initial screen. This exercises the signed minified release when signing is configured. Windows tests its published Native Messaging executable and installer install/upgrade/removal. All builds and tests must pass before the publishing job creates both the tag and GitHub Release. This avoids relying on a GITHUB_TOKEN-created tag to trigger another workflow ([GitHub documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow)). PRs run component checks without publishing or receiving signing secrets. AMO signing runs only for an eligible release, never on a documentation-only push that reuses the previous version.

Conventional Commits control versioning: `fix:`/`perf:` increment patch, `feat:` increments minor, and `!` or a `BREAKING CHANGE:` footer increments major. Documentation/chore-only changes do not publish. Initial eligible delivery is `0.1.0-alpha.1`. Every later eligible delivery increments the relevant core version; prerelease channels append `-alpha.1` or `-beta.1`.

Default channel is alpha. Repository variable `RELEASE_CHANNEL` can be `alpha`, `beta` or `stable`. Only set `PHYSICAL_VALIDATION_APPROVED=true` after recording actual device/browser installation, pairing, playback and update results. Stable publication refuses missing distribution signatures or unrecorded physical validation. `workflow_dispatch` provides an exceptional manual run; `publish=true` requests a patch increment even without an eligible commit.

Build-time version inputs stamp all artifacts consistently. Firefox requires a numeric add-on version, so its manifest uses the extension's documented SemVer mapping. Android release versionCode uses the release workflow run number. Do not reset it or replace the distribution key when updating installed clients.

## Signing configuration

Set repository Actions secrets:

| Component | Secrets | Setup |
| --- | --- | --- |
| Android | `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` | Create and back up a private release keystore using Android's official signing process. Base64-encode its bytes for the first secret. Keep the same key for updates. |
| Windows | `WINDOWS_SIGNING_PFX_BASE64`, `WINDOWS_SIGNING_PASSWORD` | Supply an appropriate Authenticode PFX with its private key. The workflow signs the published executable and installer with SHA-256 and timestamping. Hardware-backed signing services need a separate integration. |
| Extension | `MOZILLA_API_KEY`, `MOZILLA_API_SECRET` | Create Mozilla Add-on Developer API credentials and accept required distribution terms. The workflow uses Mozilla unlisted signing via web-ext. Initial account/review requirements may delay signing. |

References: [Android app signing](https://developer.android.com/studio/publish/app-signing), [Authenticode](https://learn.microsoft.com/en-us/windows/win32/seccrypto/authenticode), [Mozilla signing and distribution](https://extensionworkshop.com/documentation/publish/signing-and-distribution-overview/).

Temporary keystores/certificates are removed after jobs. Do not commit key material, upload it as a build artifact or disable signature enforcement to make an extension install normally.

### Local credential setup

Use PowerShell 7, an authenticated GitHub CLI and the GitHub account explicitly selected for the current session. The setup script checks that the active CLI account matches the supplied account before any writes. Secret values are sent to GitHub through standard input, never command-line arguments or console output.

```powershell
# Create once, or reuse, the persistent Android release identity (JDK 17 required).
./scripts/configure-signing.ps1 -Component Android -GitHubAccount hugo2006alm

# After creating a Mozilla developer account and obtaining API credentials:
./scripts/configure-signing.ps1 -Component Mozilla -GitHubAccount hugo2006alm

# Only when you have a trusted Authenticode PFX with an exportable private key:
./scripts/configure-signing.ps1 -Component Windows -GitHubAccount hugo2006alm -PfxFile C:/private/publisher.pfx
```

Android setup is configured for this repository. Its RSA-4096 signing identity has 30-year validity and is stored outside Git at `%LOCALAPPDATA%/BedsideRemote/Signing/android`, restricted to the current Windows user and SYSTEM. Its password is encrypted with current-user Windows DPAPI. Re-running setup reuses the key; it never silently replaces an incomplete or existing identity. Back up the keystore **and the recoverable password** to a password manager before losing/reinstalling the Windows profile; copying the DPAPI password file alone is not a portable backup.

The public certificate fingerprint is pinned in `android/signing-certificate.sha256`. CI verifies the signed APK against that pin and records it in the release manifest's Android signing report. Changing the pin or replacing the key requires an explicit signing migration decision. Upgrading from the initial debug-key alpha to the release-key APK requires uninstalling the debug version once, which removes its saved phone pairings; subsequent release-key updates retain the signing identity.

For Mozilla, first sign in through [the Developer Hub](https://addons.mozilla.org/en-US/developers/), review any requested distribution terms and then open [the developer API credentials page](https://addons.mozilla.org/en-US/developers/addon/api/key/) in the same signed-in browser session. If the direct page returns Not Found, enter through the Developer Hub first; the localized English URL works after signing in. Run the Mozilla setup command in your own terminal; both inputs are hidden. Do not paste credentials into chat. Signing is not complete until Mozilla returns an approved signed XPI; initial review may require user action.

Windows setup accepts an existing trusted code-signing certificate and checks its chain/private key before upload. A locally generated self-signed Windows certificate does not provide a publicly trusted publisher identity. See [Microsoft's signing options](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options) for certificate/service or Store distribution routes. Purchasing, identity verification and account terms require the publisher. The unsigned EXE remains usable for local testing; it does not satisfy the stable signing gate.

Android signing and Mozilla unlisted signing are configured and verified in [v0.1.1-alpha.1](https://github.com/hugo2006alm/bedside-remote/releases/tag/v0.1.1-alpha.1). A trusted Windows publisher certificate and physical Android/browser acceptance evidence remain outstanding. Keep the release channel in alpha until these dependencies are actually resolved. Signed Android and XPI artifacts can already be used for that acceptance testing; Windows testing can use the explicitly unsigned installer.

Distribution packages include the user-approved project MIT license (copyright hugo2006alm) and separate third-party notices. Keep the Windows runtime notices synchronized with the pinned .NET SDK; Android bundles project, runtime and font notices in assets.

### Free SignPath Foundation route

The [Code signing policy](code-signing.md) contains the prepared application, XML templates, required roles/MFA and activation instructions. Acceptance and manual signing approval are external requirements. Run `./scripts/configure-signing.ps1 -Component SignPath -GitHubAccount hugo2006alm` only after acceptance and production-policy setup. It stores a hidden submitter token, organization/project/policy and pinned certificate, then activates SignPath. CI signs the agent before packaging and signs the installer afterwards, verifying both signatures. PR and non-release builds do not call the service; a configured signing failure blocks publication.

## Artifacts and incomplete releases

The Edge companion is currently a separate unpacked development ZIP with `edge-signing.json` reporting unsigned. It is distinct from the Mozilla-signed XPI. Stable validation now includes Edge as a separate distribution gate: completing Windows signing alone cannot promote a developer-mode Edge package to stable. Normal Edge Add-ons distribution remains pending.

With configured signatures, releases contain `bedside-remote-android-vX.Y.Z.apk`, `bedside-remote-windows-x64-vX.Y.Z-setup.exe` and `bedside-remote-extension-vX.Y.Z.xpi`, plus extension source, signing reports, a compatibility/validation manifest and SHA-256 checksums.

Without credentials, alpha/beta releases explicitly contain a debug-key development APK, an unsigned Windows installer and a development extension ZIP for temporary loading. These are not represented as signed distribution builds. A new CI debug key can prevent in-place Android updates; uninstalling loses local pairing settings. Use a persistent release key for distribution.

The release manifest reports signing and physical validation independently. A passing CI run is not a stable readiness assertion. Standard GitHub-hosted runners are used; no paid larger runners are configured. Artifact retention is kept short.
