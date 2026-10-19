# Android app

Native Kotlin / Jetpack Compose / Material 3. Requires Android 8.0 (API 26) or newer. Build with JDK 17 and Android SDK 36.

```sh
./gradlew spotlessCheck lint testDebugUnitTest assembleDebug
./gradlew connectedDebugAndroidTest # a running emulator or connected device is required
```

Use `gradlew.bat` on Windows. Run `spotlessApply` to format Kotlin. Debug APK: `app/build/outputs/apk/debug/app-debug.apk`. Unit and TLS loopback test reports: `app/build/reports/tests/testDebugUnitTest/`. Compose previews cover midnight and daylight with large text; instrumentation tests exercise accessible D-pad/fingerprint controls, retained keyboard drafts and session recovery.

Release builds accept `-PversionName=X.Y.Z -PversionCode=N`. Set `ANDROID_KEYSTORE_PATH`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` to sign `assembleRelease`. Without these variables, the release APK is unsigned. Do not commit a keystore or `local.properties`.

## Use

Open the Windows pairing window, then choose a discovered computer or enter its numeric private IP address and port. Enter the complete SHA-256 fingerprint shown on Windows, compare every digit, enter the six-digit code, and approve the phone on Windows. Discovery does not establish trust. Tokens and computer details are encrypted with an Android Keystore AES-GCM key; backups are disabled.

Discovery considers all resolved addresses on Android 14 and later, with optional bounded Windows TXT hints for earlier Android versions. It rejects public, CGNAT and loopback discovery addresses and prefers an address on one of the phone's local subnets. A discovered address still requires the complete certificate fingerprint and Windows approval.

Remote works independently of the browser extension. Touch offers pointer movement, tap, long-press/right click and two-finger scroll, plus explicit accessible click/scroll buttons. Keyboard sends Unicode text to the focused Windows field. Share an official HTTPS YouTube link to Bedside Remote or paste one in Queue, then choose Play now, Play next or Add to queue. Queue changes are acknowledged by Windows; playback may still require a browser gesture because of autoplay restrictions.

If the PC’s IP changes, use **Devices → Edit address**. Android checks the saved certificate before saving the new numeric address/port, preserving pairing. Text and pasted video drafts remain until Windows acknowledges success; rejection/disconnect preserves them for review, without automatic replay.

App connections close while backgrounded and reconnect on return. Commands are never replayed after reconnect. Revoke phones in Windows; Forget removes credentials from this phone only.

## Visual direction

Midnight minimal luxury: charcoal surfaces, warm ivory text, restrained brass accent, Manrope display type and DM Sans body type. A concentric circular D-pad anchors the design, with frequent actions in the lower thumb zone. Design feasibility score: 14 (impact 4, fit 5, feasibility 4, performance 4, consistency risk 3). Touch feedback uses a short native spring; system animation scale and explicit Reduce motion are respected. Light appearance, optional haptics and pointer sensitivity are available in Settings. Fonts are bundled offline with their OFL notices under app assets; the notices apply to the fonts, not the application source.

Physical validation remains necessary for LAN discovery, device installation, pairing with Windows, TalkBack, large display/font scales, keyboard/Unicode in target apps, touch ergonomics and performance. Browser playback requires real Zen/Firefox YouTube and Edge Netflix Store-app testing. Compilation or loopback tests do not establish those results.
