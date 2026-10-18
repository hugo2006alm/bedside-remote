# Privacy policy

Bedside Remote is maintained by **hugo2006alm**. This policy covers the Android app, Windows agent and browser companions. Last updated: 2026-10-18.

## Local control and data

Remote commands, including typed text, are sent over certificate-pinned encrypted connections to the Windows computer the user explicitly paired and approved. Commands are not sent to a developer-operated server. The product has no analytics, advertising, cloud relay or automatic crash-reporting service.

Android stores computer addresses, certificate fingerprints, pairing credentials and preferences on the device; pairing details are encrypted using Android Keystore. Windows stores its TLS identity, device credential hashes, settings and the YouTube queue in the user's local app-data directory, using Windows DPAPI for protected identity/credential data. Typed text is used for the requested desktop input and is not intentionally logged or persisted by the Windows agent.

With the browser companion installed and site access granted, compatible YouTube/Netflix players report titles, playback state, available controls, and YouTube video IDs, channel names and thumbnail links to the local agent and its paired phones. This includes compatible player sessions used for target selection; it is not general browser-history collection. Streaming account credentials, cookies, payment details and decrypted media are not collected or transmitted by Bedside Remote.

## User-requested external connections

Android may download player thumbnails from the image URLs reported by YouTube. The image provider receives normal network request information, such as the requesting IP address. Playback/navigation remains in the user's browser and follows the streaming provider's own privacy policy: [YouTube/Google](https://policies.google.com/privacy) and [Netflix](https://help.netflix.com/legal/privacy).

The Windows release-check command opens the project's GitHub releases page in the user's browser, subject to [GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). Code-signing/build services are used during development and release, not as a runtime control service.

## Control, retention and deletion

The user chooses whether to install the optional browser companion and which devices to approve. Revoke a device on Windows to close its active connections and invalidate access; use Forget on Android to remove its local pairing. Clear the queue to delete its saved entries. Android uninstall/app-data removal deletes its stored data. Windows deliberately preserves `%LOCALAPPDATA%\BedsideRemote` across upgrades and normal uninstall; delete that directory yourself to remove retained identity, settings, queue and pairings. Removing the TLS identity requires verifying a new fingerprint before pairing again.

Browser player snapshots are held in memory, rather than saved as browsing history by the companion. The operating system, target applications and streaming sites can have their own independent storage or diagnostics policies.

For a privacy question, use [the repository's issue tracker](https://github.com/hugo2006alm/bedside-remote/issues). Do not post pairing credentials, certificates with private keys, typed private text or account details in a public issue.
