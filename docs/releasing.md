# Builds and releases

## Automatic flow

Each push to main runs version calculation and calls Android, Windows and extension CI. Android also runs Compose tests on an API 35 emulator and captures its real initial screen; Windows tests its published Native Messaging executable and installer install/upgrade/removal. All builds and tests must pass before the publishing job creates both the tag and GitHub Release. This avoids relying on a GITHUB_TOKEN-created tag to trigger another workflow ([GitHub documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow)). PRs run component checks without publishing or receiving signing secrets. AMO signing runs only for an eligible release, never on a documentation-only push that reuses the previous version.

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

## Artifacts and incomplete releases

With configured signatures, releases contain `bedside-remote-android-vX.Y.Z.apk`, `bedside-remote-windows-x64-vX.Y.Z-setup.exe` and `bedside-remote-extension-vX.Y.Z.xpi`, plus extension source, signing reports, a compatibility/validation manifest and SHA-256 checksums.

Without credentials, alpha/beta releases explicitly contain a debug-key development APK, an unsigned Windows installer and a development extension ZIP for temporary loading. These are not represented as signed distribution builds. A new CI debug key can prevent in-place Android updates; uninstalling loses local pairing settings. Use a persistent release key for distribution.

The release manifest reports signing and physical validation independently. A passing CI run is not a stable readiness assertion. Standard GitHub-hosted runners are used; no paid larger runners are configured. Artifact retention is kept short.
