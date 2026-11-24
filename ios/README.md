# iOS client

Bedside Remote for iPhone uses SwiftUI for the native tab bar, toolbars, menus, sheets and appearance settings. On iOS 26, system components adopt Liquid Glass from the OS. The touchpad uses a plain system surface and a UIKit touch view. The deployment target is iOS 17. SwiftUI and UIKit keep touch sampling, VoiceOver, the native keyboard, local networking and Shortcuts close to their platform APIs. Flutter would add a runtime plus native bridges for these integrations. Kotlin Multiplatform could share models and transport policy after a refactor, but the current Android code depends on platform-specific discovery, transport and storage. Both mobile clients share the protocol and link-validation fixtures today.

Windows owns playback, sessions, the queue and revocation. The iPhone discovers private endpoints with Bonjour or accepts a manual numeric address. Compare the full SHA-256 fingerprint from Windows before requesting pairing, then approve the request on Windows. Both HTTPS and WSS pin the leaf certificate. Pairings are stored in the device-only Keychain and commands are discarded when a socket closes. Allow Local Network in iOS Settings if discovery or pairing fails. Open the Windows pairing window again if the six-digit code expires. Re-pair after a known certificate reset and verify the new fingerprint locally.

With several pairings, the app checks saved endpoints in parallel when returning to the foreground. It connects automatically when exactly one computer responds with the expected certificate. When several respond or none responds, choose in Devices or check availability again. If a selected connection fails, Devices offers alternatives while that connection retries. Remote commands stay bound to the selected computer.

## Build and install

1. Run the Windows setup executable from the [releases page](https://github.com/hugo2006alm/bedside-remote/releases). Allow the agent on your trusted private network if Windows Firewall prompts. For contextual playback, load the companion for the appropriate browser. See the [Windows guide](../windows/README.md) and [Netflix setup](../docs/netflix.md).
2. Download the **ios-sideload** artifact from the branch's **iOS CI** run and extract the IPA. Compare its SHA-256 with the included checksum. On Windows, use `Get-FileHash -Algorithm SHA256 -LiteralPath "path-to.ipa"`.
3. On the iPhone, install LocalDevVPN and connect it. On Windows, install iTunes and iLoader using the [SideStore prerequisites](https://docs.sidestore.io/docs/installation/prerequisites). Connect the iPhone by USB and confirm the trust prompt on the phone. Open iLoader, sign in with your Apple Account, select the iPhone and choose **Install SideStore (Stable)**.
4. On the iPhone, open **Settings → General → VPN & Device Management** and trust the developer entry for the same Apple Account. Follow any restart and passcode prompts. Enable **Developer Mode** in **Settings → Privacy & Security**, restart and confirm. Connect LocalDevVPN, open SideStore, sign in with the same account and refresh SideStore under **My Apps**. Use the [SideStore install guide](https://docs.sidestore.io/docs/installation/install) for the trust and restart sequence on your iOS version.
5. Transfer the IPA to Files on the iPhone. With LocalDevVPN connected, open **SideStore → My Apps → +**, select the IPA and finish signing on the device.
6. Open Bedside Remote, allow Local Network access and put the iPhone and Windows on the same trusted LAN. In Windows, open the pairing window. On the phone, open **Devices**, choose the discovered PC or enter its numeric address and port, compare all 64 fingerprint digits, enter the temporary code, and approve the request on Windows.

CI uses the pinned Xcode and XcodeGen versions listed in the workflow. On macOS with those tools installed, run `bash ios/build.sh` for simulator tests, screenshots and the device IPA, or `bash ios/build.sh --package-only` for the IPA alone. GitHub's runner performs these steps for Windows users. The device IPA is ARM64 and prepared for SideStore re-signing. Its CI tests run on a simulator.

[Apple Personal Team provisioning expires after seven days](https://developer.apple.com/support/compare-memberships/). Refresh through SideStore before expiry. For an update, install the newer IPA over the existing app using the same Apple account and signing identity. A changed effective app identifier or Keychain access group may make pairings inaccessible. If SideStore's signature expires, restore or reinstall SideStore using its current recovery guide, then refresh Bedside Remote. Revoke any old iPhone entry in Windows before replacing a pairing you can no longer access.

## Remote and sharing

Drag one finger to move, tap to click, use two fingers for right-click or scrolling, and double-tap and hold to drag. The Windows controls sheet includes volume, media, Start and directional navigation. The selected browser session appears below the touchpad. Playback and episode buttons follow the capabilities reported by the browser. A short-screen or accessibility-size layout opens playback in a sheet. Settings offers System, Light and Dark themes and optional haptics.

Paste an official YouTube video URL in **Queue** and select **Play now**, **Play next** or **Add to queue**. Shortcuts can pass a URL to **Prepare a YouTube link**. A shortcut can also URL-encode a link as the `url` parameter of `bedsideremote://youtube?url=…` and open that URL. Both routes only prepare a draft. Confirm the desired operation inside Remote. The initial SideStore package contains the main app only. Direct YouTube share-sheet integration remains pending validation of shared storage and signing privileges for an embedded Share Extension.

## Physical acceptance

- Install and refresh under a Personal Team through SideStore on a physical iPhone; check the icon, launch, updates and pairing retention on an over-install.
- Test discovery, manual pairing, Windows denial, mismatched fingerprint, Local Network denial, revocation and certificate replacement.
- Pair two computers. Switch during playback, while dragging and with drafts pending. Reopen after lock, Wi-Fi changes or Windows restart. Check input is released and old commands are not resent.
- Test pointer, click, two-finger gestures, drag and Unicode input in real Windows windows. Test VoiceOver, one-handed use, Dynamic Type, orientation and appearance changes.
- Verify real Zen/Firefox YouTube and the Edge Netflix Store PWA. Check session selection, play/pause, seek, fullscreen exit, optional episode/audio controls, queue ordering and natural advance. Exercise both Shortcuts and paste.

## Android/iOS parity

| Capability | Android | iPhone |
| --- | --- | --- |
| Remote and browser playback | Touchpad, Windows controls and contextual streaming actions | Same protocol; touchpad uses UIKit and system sheets |
| Queue authority | Windows | Windows |
| YouTube entry | Android share sheet or paste | Shortcuts, validated deep link or paste |
| Connection | Android NSD, numeric address, full fingerprint, Keystore | Bonjour, numeric address, full fingerprint, Keychain |
| Device management | Select, update address, forget; revoke on Windows | Select, reconnect, forget; revoke on Windows |
| Appearance | Midnight or light, haptics and explicit reduce motion | System, dark or light, haptics and native accessibility preferences |
| Distribution | Android APK | ARM64 prerelease IPA for SideStore |

## SideStore source

After the first iOS prerelease is published, add the [Bedside Remote source](https://raw.githubusercontent.com/hugo2006alm/bedside-remote/sidestore/sidestore-source.json) in SideStore under **Sources**. The release workflow refreshes it with the latest published IPA, exact app version and build number, publication date, size and digest. This is a public GitHub URL and requires no additional service. For test builds from a pull request, use the IPA file from the Actions artifact.
