# Roadmap

Practical reference for work outside the MVP. Listing an item is not evidence of implementation or validation.

## MVP — required for the first release

- **Universal remote:** media/volume, D-pad/navigation, trackpad gestures and Android keyboard with Unicode.
- **Connection/security:** discovery and manual connection, explicit pairing approval, encryption/authentication, reconnect, device management and revocation.
- **YouTube in Zen/Firefox:** Android sharing, remote playback, synchronized editable queue, automatic advance when permitted, metadata and contextual controls.
- **Netflix:** contextual player/progress, episode/intro/recap controls when available; stable audio/subtitle access if viable; universal fallback. Real Zen playback/DRM compatibility requires separate validation.
- **Quality/distribution:** accessible polished native UI and motion, reduced motion, Windows tray agent, optional extension, tests, CI, installers, automatic releases and concise documentation. Signing credentials and physical validation are release dependencies, not future features.

Required sensitivity adjustments and basic interaction feedback do not depend on advanced customization.

## Post-MVP — planned features

| Feature | Priority | Dependencies | Acceptance criteria | Status |
| --- | --- | --- | --- | --- |
| Wake-on-LAN from Android. | P1 | Saved network details; compatible hardware, firmware, Windows and network. | Send a magic packet; wake only from verified supported states; document configuration and limits. | Planned |
| Power management: lock, suspend, shut down and restart. | P2 | Authenticated connection, authorized agent actions and disconnect handling. | Execute allowed actions only; confirm destructive actions; reflect disconnection/recovery. | Planned |
| Advanced customization: favorites, quick actions and sensitivity/behavior/haptic preferences. | P2 | Stable core controls, persisted settings and an allowed-action policy. | Persist preferences across sessions; restore defaults; execute authorized shortcuts only. | Planned |

## Ideas under evaluation — implementation not yet approved

For these items, “planned” refers to investigation, not a delivery commitment. Define open acceptance criteria after assessing feasibility.

| Possibility | Priority | Dependencies / investigation | Acceptance criteria | Status |
| --- | --- | --- | --- | --- |
| Native YouTube search using an official solution. | P1 | Evaluate YouTube Data API credentials, quotas, policies and maintenance. Must not delay required sharing/queue flows. | If approved: search and play now/next/add to queue; handle errors and quota limits. | Planned |
| QR-assisted pairing to avoid manually entering the full certificate fingerprint. | P2 | Evaluate a native scanning flow, camera permissions and trusted transfer of the endpoint/fingerprint. Temporary-code pairing remains the MVP flow. | If approved: fill connection details from a Windows-displayed QR code; retain identity verification, expiry and explicit local approval. | Planned |
| Chrome/Edge extension, including possible Netflix fallback in Edge. | P2 | WebExtensions/Native Messaging compatibility, distribution and real DRM/playback tests; preserve Zen/Firefox quality. | Define after compatibility investigation. | Planned |
| Silent automatic updates. | P3 | Reliable signing/distribution, protocol compatibility and preserved pairings/settings. | Define after investigation. Checking the latest version and opening its release is sufficient for MVP. | Planned |
| Portable Windows distribution. | P3 | Concrete benefit over installer; Native Messaging registration and persisted data without installation. | Define after evaluating setup, updates and removal. | Planned |

## Maintenance

- **Relative future-work priorities:** P1 high, P2 medium, P3 low. None blocks the first release.
- **Allowed statuses:** planned, in development, completed, discarded.
- Record newly identified work outside the MVP immediately. Keep unresolved dependencies explicit and acceptance criteria only as detailed as currently justified.
- Update in the same change that changes scope or status. Mark completed items with a delivery reference when available or remove them when no longer useful.
- Promote an idea to post-MVP only after a scope decision. Changing mandatory MVP requirements requires an explicit user decision.
- Keep entries short; detailed technical decisions belong in their own documentation.
