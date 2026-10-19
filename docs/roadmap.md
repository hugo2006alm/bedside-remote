# Roadmap

Practical reference for work outside the MVP. Listing an item is not evidence of implementation or validation.

## MVP — required for the first release

- **Universal remote:** media/volume, D-pad/navigation with browser focus selection and Windows keyboard fallback, trackpad gestures and Android keyboard with Unicode.
- **Connection/security:** discovery and manual connection, explicit pairing approval, encryption/authentication, reconnect, device management and revocation.
- **YouTube in Zen/Firefox:** Android sharing, remote playback, synchronized editable queue, automatic advance when permitted, metadata and contextual controls.
- **Netflix:** contextual player/progress, episode/intro/recap controls when available; stable audio/subtitle access if viable; universal fallback. The user approved an Edge companion for the Store PWA because Zen on Windows cannot provide its DRM playback. Live Store-app compatibility remains a validation requirement.
- **Quality/distribution:** accessible polished native UI and motion, reduced motion, Windows tray agent, optional extension, tests, CI, installers, automatic releases and concise documentation. Signing credentials and physical validation are release dependencies, not future features.

Required sensitivity adjustments and basic interaction feedback do not depend on advanced customization.

## Post-MVP — planned features

| Feature | Priority | Dependencies | Acceptance criteria | Status |
| --- | --- | --- | --- | --- |
| Approved media launcher from the remote's grid button. | P1 | Authenticated agent catalog; fixed app identities and official web destinations. | Open YouTube and Netflix; show installed Spotify, Stremio and VLC where detected; reject arbitrary paths/URLs and preserve per-PC targeting. | Completed in [PR #12](https://github.com/hugo2006alm/bedside-remote/pull/12); simulated launch, protocol/authentication and two-PC targeting checks pass; physical launch acceptance pending. |
| Wake-on-LAN from Android. | P1 | Saved network details; compatible hardware, firmware, Windows and network. | Send a magic packet; wake only from verified supported states; document configuration and limits. | Planned |
| Power management: lock, suspend, shut down and restart. | P2 | Authenticated connection, authorized agent actions and disconnect handling. | Execute allowed actions only; confirm destructive actions; reflect disconnection/recovery. | Planned |
| Advanced customization: favorites, quick actions and sensitivity/behavior/haptic preferences. | P2 | Stable core controls, persisted settings and an allowed-action policy. | Persist preferences across sessions; restore defaults; execute authorized shortcuts only. | Planned |

## Ideas under evaluation — implementation not yet approved

For these items, “planned” refers to investigation, not a delivery commitment. Define open acceptance criteria after assessing feasibility.

| Possibility | Priority | Dependencies / investigation | Acceptance criteria | Status |
| --- | --- | --- | --- | --- |
| Native YouTube search using an official solution. | P1 | Evaluate YouTube Data API credentials, quotas, policies and maintenance. Must not delay required sharing/queue flows. | If approved: search and play now/next/add to queue; handle errors and quota limits. | Planned |
| QR-assisted pairing to avoid manually entering the full certificate fingerprint. | P2 | Evaluate a native scanning flow, camera permissions and trusted transfer of the endpoint/fingerprint. Temporary-code pairing remains the MVP flow. | If approved: fill connection details from a Windows-displayed QR code; retain identity verification, expiry and explicit local approval. | Planned |
| Chrome distribution of the browser companion. | P2 | Reuse the Edge-compatible package; decide store identity/distribution and validate in real Chrome. Edge support for the Netflix Store PWA is now approved MVP work. | Define after distribution and compatibility investigation. | Planned |
| Silent automatic updates. | P3 | Reliable signing/distribution, protocol compatibility and preserved pairings/settings. | Define after investigation. Checking the latest version and opening its release is sufficient for MVP. | Planned |
| Portable Windows distribution. | P3 | Concrete benefit over installer; Native Messaging registration and persisted data without installation. | Define after evaluating setup, updates and removal. | Planned |
| Optional clipboard-assisted insertion for long text. | P3 | Investigate explicit opt-in, clipboard formats/history, restoration races and target-app compatibility; retain current keyboard input. | Define after feasibility/privacy review; no clipboard contents may be transmitted back to Android. | Planned |

## Maintenance

- **P1 — open development dependency advisory:** update the pinned web-ext dependency when upstream supplies a compatible node-forge fix; dependencies: upstream patched release; acceptance: advisory absent and lint/package/signing checks pass; status: planned. [Current assessment](security.md#open-development-dependency-advisory).

- **Relative future-work priorities:** P1 high, P2 medium, P3 low. None blocks the first release.
- **Allowed statuses:** planned, in development, completed, discarded.
- Record newly identified work outside the MVP immediately. Keep unresolved dependencies explicit and acceptance criteria only as detailed as currently justified.
- Update in the same change that changes scope or status. Mark completed items with a delivery reference when available or remove them when no longer useful.
- Promote an idea to post-MVP only after a scope decision. Changing mandatory MVP requirements requires an explicit user decision.
- Keep entries short; detailed technical decisions belong in their own documentation.
