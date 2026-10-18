# Code signing policy

Project owner, committer/reviewer and intended signing approver: [hugo2006alm](https://github.com/hugo2006alm). Project source is MIT-licensed; copyright remains with hugo2006alm. Third-party components retain their own notices.

## Free Windows signing: application pending

The [SignPath Foundation](https://signpath.org/terms.html) offers free signing for accepted open-source projects. Acceptance depends on its review, including project reputation; this new project is not guaranteed acceptance. No subscription, certificate or approval has been obtained yet. Windows releases remain explicitly unsigned until verified production signatures exist.

The Foundation's certificate identifies SignPath Foundation as publisher, not the project owner's personal identity. This does not transfer project copyright. After acceptance, publish its required credit: “Free code signing provided by SignPath.io, certificate by SignPath Foundation”, with links to both providers; do not claim this service has been granted before approval.

All team members must use MFA on GitHub and SignPath. Restrict the SignPath GitHub App to this repository. Link the project to GitHub as its trusted build system, require the protected main branch and **manual approval of every signing request**. The submitter token must not have approval rights.

## Implementation and activation

The release workflow selects `none`, legacy `pfx`, or `signpath`. PRs and documentation-only builds cannot request signatures. Selecting SignPath with incomplete configuration fails the build; it cannot silently fall back to unsigned output.

1. Apply through [SignPath Foundation](https://signpath.org/apply.html). Use the application details below and complete the provider's account/terms steps yourself.
2. Once accepted, link the approved organization/project to this repository and create artifact configurations `windows-agent` and `windows-installer` from [the XML templates](../windows/signing/). Confirm the templates against the service's schema and approved sample binaries. Each archive contains exactly the relevant project binary, with product name/version restrictions. Production certificate, signing policy and trusted-build constraints require approval by the provider.
3. Obtain the approved organization ID, project/policy slugs, production certificate's SHA-1 thumbprint and a submitter API token. The thumbprint pins the certificate; binaries are signed with SHA-256 and a trusted timestamp.
4. In PowerShell 7, run `./scripts/configure-signing.ps1 -Component SignPath -GitHubAccount hugo2006alm`. It prompts for the organization ID, public certificate thumbprint and hidden token. Project/policy defaults are `bedside-remote` / `release-signing`; override the corresponding `-SignPathProjectSlug` and `-SignPathSigningPolicySlug` parameters if the provider assigns different slugs. Never paste the token into chat.
5. A subsequent eligible release submits the agent first. After manual approval, CI verifies the chain, timestamp and pinned publisher, packages that signed executable, then submits the installer for a second manual approval. The installer must have the same publisher. Installation smoke tests verify the installed executable matches the packaging input. Only then can the signing report become true.

Required repository configuration: secret `SIGNPATH_API_TOKEN`; variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_CERTIFICATE_THUMBPRINT`, and `WINDOWS_SIGNING_PROVIDER=signpath`. The setup helper activates the provider last. Live signing remains untested until the service accepts the project. Never use a test certificate or remove manual approvals to make CI pass.

## Application details to copy

- Project: **Bedside Remote**
- Owner/maintainer/approver: **hugo2006alm**
- Source and homepage: https://github.com/hugo2006alm/bedside-remote
- Releases: https://github.com/hugo2006alm/bedside-remote/releases
- License: MIT; copyright (c) 2026 hugo2006alm.
- Description: Native Android companion controlling a Windows desktop over an encrypted, authenticated local network, with explicit device approval, universal input controls and optional Zen/Edge media integrations. No remote shell, credential collection or DRM bypass.
- Windows artifacts: own `BedsideRemote.exe` and Inno Setup installer; self-contained Microsoft runtime and third-party DLLs retain their own signatures/notices and are not re-signed as project code.
- Build: GitHub Actions, versioned source, pinned dependencies/actions, tests and installer smoke checks before publication.
- Privacy: commands/player metadata are exchanged only with the local Windows agent, the user's browser and explicitly paired phones. A user-requested release check opens GitHub. The software has no analytics or cloud control relay; Android may request official YouTube thumbnails when displaying them. Netflix/YouTube playback remains in the user's own browser/service session under those services' privacy policies. Streaming credentials and cookies are never collected or sent to Android.
- Contact: supply your own email in the provider's form; it is intentionally not committed here.

## Other free route

[Microsoft documents free Store signing for MSIX](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options) and [free developer onboarding](https://learn.microsoft.com/windows/apps/publish/partner-center/open-a-developer-account). This requires Store packaging, identity verification and certification. It does not sign the existing downloadable EXE installer. MSIX distribution is not implemented; it is an alternative if SignPath rejects the application, subject to a separate packaging decision.
