# Implementation plan

The supplied Bedside Remote implementation brief is the product scope. This document records its execution order, including the roadmap additions to sections 15 and 16. It does not replace implementation with documentation.

1. **Research and design:** validate Android, Windows, Zen/Firefox, Native Messaging, local discovery, pairing, streaming constraints and distribution against official documentation. Establish architecture, security, visual direction and versioned contracts. Create the repository under the selected GitHub account, `hugo2006alm`. The user subsequently requested public visibility to use standard GitHub Actions runners without private-repository minutes.
2. **Foundations:** create native Android and interactive Windows applications; implement local encrypted communication, discovery, explicit pairing, authentication, revocation and reconnect. Establish CI for all components.
3. **Universal remote:** implement navigation, Unicode keyboard, trackpad gestures, volume and media controls; keep this usable without the extension. Test input validation and transport independently from physical input.
4. **YouTube:** implement Zen/Firefox extension, Native Messaging, Android share receiver, remote playback, persistent queue, editing and synchronization. Handle natural completion, manual navigation, tab changes, reconnect and autoplay restrictions.
5. **Netflix:** implement isolated semantic adapter, contextual capabilities, progress and episode/intro controls; retain universal fallback. Validate real DRM/browser support separately from fixture tests.
6. **Visual refinement:** refine the dark-first minimal luxury design introduced from the start: ergonomic D-pad, quiet contextual media panel, native Compose motion, immediate feedback, reduced motion, accessibility and empty/error states. Validate on real devices when available.
7. **Complementary features:** consult [roadmap.md](roadmap.md). Any complementary feature that jeopardizes MVP delivery moves to the roadmap and must not block the first release. Research native YouTube search without delaying required sharing and queue flows.
8. **Release:** complete component CI, automatic SemVer calculation, installation packages, configured signing, checksums and automatic release publication from eligible changes on main. Keep incomplete signing or outstanding physical validation explicit; publish only an appropriate prerelease until validated.

## Documentation and roadmap (brief section 15)

Maintain concise README, AGENTS, architecture and security documentation with real build/test commands. Maintain `docs/roadmap.md` as the practical record of planned work outside the MVP. Each item includes a short description, relative priority, dependencies, acceptance criteria when defined, and status: planned, in development, completed or discarded. Clearly distinguish mandatory MVP, approved post-MVP work and ideas awaiting investigation/approval. Update or remove implemented items as the project evolves. AGENTS instructs agents to consult and maintain the roadmap without duplicating its feature list.

## Delivery evidence

Report repository URL, architecture and stack, skills used, implemented functionality, CI/release state, installable artifacts, setup/pairing instructions, executed automated/integration/physical tests and remaining external requirements. Never claim physical Netflix/YouTube validation from fixtures or a passing build.
