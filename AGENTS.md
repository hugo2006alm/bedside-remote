# Bedside Remote — agent instructions

## Product and architecture

Build a native Android remote for Windows, with universal controls and dedicated YouTube/Netflix integrations in Zen/Firefox. Android uses Kotlin/Compose; the interactive Windows agent uses C#/.NET; the optional extension uses TypeScript/WebExtensions. Consult [the implementation plan](docs/implementation-plan.md), [architecture](docs/architecture.md), [security](docs/security.md) and [protocol v1](contracts/protocol.md). All repository content must be in English.

## Scope and roadmap

- Read [docs/roadmap.md](docs/roadmap.md) before planning or implementing changes. Keep future feature details there rather than duplicating them here.
- Record useful work outside the MVP immediately, with a short description, relative priority, dependencies, acceptance criteria when defined, and status.
- Move complementary features that jeopardize MVP delivery to the roadmap; they must not block the first release. Do not defer mandatory MVP requirements without an explicit user decision.
- Update the roadmap in the same change that starts, completes or discards an item. Completion requires implementation and validation evidence; retain a delivery reference when useful or remove completed items.
- Investigate and decide scope before implementing ideas under evaluation. Keep the roadmap concise, not a progress diary or detailed specification.

## Engineering rules

- Security, accessibility, refined visuals and native motion are requirements from the start. Maintain a usable universal remote without the extension.
- Follow the versioned contract across all components. Reject unknown actions and invalid payloads. Isolate streaming adapters and expose only currently available capabilities.
- Require encrypted authenticated LAN transport, explicit Windows pairing approval, secure credential storage and revocation. No arbitrary remote shell, streaming credentials, private catalog APIs or DRM bypass.
- Prefer platform APIs and minimal justified dependencies. Do not add an open-source project license without user authorization.
- Run relevant builds, lint and tests; distinguish automated tests, integration tests, physical-device tests and pending validation. Never infer real streaming compatibility from fixtures.
- Keep CI and automatic releases working. Tags and releases must be created in the same supported flow; never rely on a GITHUB_TOKEN-created tag triggering another push workflow. Label missing signing or physical validation honestly.
- Keep README build commands and installation instructions synchronized with the implementation. See component READMEs for exact commands.

## GitHub identity

Before any GitHub operation, including read-only queries, commits, pull requests and reviews, ask which account to use only if none has already been selected or explicitly specified in this chat. Reuse the chosen account for subsequent operations and switch only on explicit user request. Ask even if only one account is configured. Repository ownership or a choice recorded in an earlier chat does not select an account for a new chat.
