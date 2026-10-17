# Security

## Trust model

The Android device can control the logged-in Windows desktop after explicit pairing. Treat it as a trusted input device. The agent runs without administrator privileges, does not expose a remote shell and accepts only typed allowlisted commands. Elevated windows, the lock screen and secure desktop are outside normal input control.

The local network and DNS-SD announcements are untrusted. A persistent self-signed server certificate is pinned by its SHA-256 DER fingerprint, verified from the Windows display before the pairing code is sent. Pairing is limited to a short local window, rate-limited and subject to local Windows approval. A discovered address or code alone never establishes server identity.

## Transport and credentials

- Use TLS for pairing and all control traffic. No cleartext fallback and no token in URL/query strings.
- Authenticate WSS with a random per-device bearer token. Protect Windows persistent secrets with current-user DPAPI and Android secrets with Keystore-backed authenticated encryption. Do not log codes, tokens or private keys.
- Enforce message size, schema, numeric ranges, known protocol version and per-connection increasing sequence numbers. TLS protects recorded transport traffic; commands are not automatically replayed after reconnect.
- Revocation rejects future access and terminates existing authenticated sockets. Changing/resetting the server certificate requires explicitly verifying its new fingerprint.
- Restrict remote control to private/link-local/loopback addresses. Do not forward the port, enable UPnP or open a public firewall profile. Only allow the Windows executable on trusted private networks when Windows prompts.

Native Messaging allows only the fixed extension ID. The bridge validates its invocation and uses a current-user-only named pipe; it is not an unauthenticated network bridge. This boundary does not defend against arbitrary malware already running as the same Windows user. Framed JSON has a bounded size; stdout carries protocol bytes only.

## Browser and input boundaries

Extension permissions are restricted to supported YouTube/Netflix domains plus required extension APIs. Streaming adapter logic reads necessary playback state and semantic controls only, never cookies, Google/Netflix credentials, private catalogs or DRM material. Site DOM changes can make controls unavailable; fall back to universal input and report the limitation.

YouTube URLs require HTTPS, exact official hosts, an accepted path and a valid video ID. Reject embedded credentials, spoofed hosts and unexpected ports. The agent independently validates incoming links and commands rather than trusting Android or extension validation alone.

## Distribution

Secrets belong in GitHub Actions secrets, never source control, artifacts or logs. Third-party Actions are pinned to verified commit SHAs; Dependabot tracks updates. PR jobs use read-only repository permissions. Publishing gets write permission only after all component checks pass.

See [releasing.md](releasing.md) for Android, Authenticode and Mozilla signing setup. Unsigned/development artifacts are explicitly labelled; stable publication requires configured signatures and recorded physical validation. Preserve signing identities and credentials across upgrades.

## Verification boundaries

Automated validation covers protocol, queue, URL, adapter and release behavior; it cannot establish physical input reliability, Windows firewall behavior, OEM keyboard behavior or Netflix DRM compatibility. Real device/browser tests remain mandatory before claiming those properties or promoting a stable release. No analytics or cloud relay is required by the control protocol.
