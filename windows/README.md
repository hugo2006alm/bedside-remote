# Windows agent

The .NET 10 LTS WPF agent runs in the signed-in user's interactive session, with a notification-area icon. It needs neither a Windows Service nor administrator rights. Closing its window keeps remote control available; **Exit** in the notification-area menu stops it.

## Build and validate

Install the .NET 10.0.402 SDK on Windows, matching the repository's `global.json`. From the repository root:

```powershell
dotnet restore windows/BedsideRemote.slnx
dotnet format windows/BedsideRemote.slnx --verify-no-changes --no-restore
dotnet build windows/BedsideRemote.slnx -c Release --no-restore
dotnet run --project windows/BedsideRemote.Tests -c Release --no-build
dotnet run --project windows/BedsideRemote.IntegrationTests -c Release --no-build
dotnet publish windows/BedsideRemote.Agent -c Release -r win-x64 --self-contained true -p:Version=0.1.0-alpha.1 -o artifacts/windows
```

The dependency-free core harness checks validation, the shared YouTube URL corpus, LAN policy, replay sequencing, expiring pairing, credential matching, queue transitions and native framing. The Windows integration harness uses real pinned HTTPS/WSS, DPAPI and a current-user named pipe with a simulated browser. Its desktop-input delegate is replaced with a counter; it never sends input to the desktop. When the installed agent is running, use `-- --isolate-native` to test on a unique pipe without interfering with the browser connection. Optional `-- --preview=artifacts/windows-preview.png` renders the actual WPF settings layout offscreen. After publishing, `-- --native-exe=artifacts/windows/BedsideRemote.exe` additionally tests the actual executable's framed stdin/stdout bridge and multiple browser peers; that mode uses the normal pipe and requires the installed agent to be stopped.

Build the per-user installer with Inno Setup 6.7 or later:

```powershell
ISCC.exe /DAppVersion=0.1.0-alpha.1 "/DPublishDir=$PWD/artifacts/windows" "/DOutputDir=$PWD/artifacts" windows/installer/bedside-remote.iss
```

Use a consistent release version across components. The self-contained installer includes the .NET runtime. Authenticode signing is performed by the release workflow when its signing credentials are configured.

The published `licenses/` directory contains the dependency and runtime notices from [windows/licenses](licenses/README.md). These notices apply only to the identified third-party components, not to the project's own source.

## Pairing and browser integration

1. Open **Pair an Android device**. First copy the SHA-256 fingerprint into Android to verify the PC's TLS identity. Then enter the six-digit pairing code.
2. Approve the named Android device in the separate Windows prompt. Pairing expires after five minutes, allows ten code checks per window and requires approval within 45 seconds. Approved attempts consume two checks to verify that the window has not expired during local approval.
3. Install the Zen/Firefox extension separately. The installer writes its Native Messaging manifest and registers `com.bedside.remote` under the current user's Mozilla host key. No manual Registry changes are needed. Firefox launches the same executable with the manifest path and authorized extension ID; `--native bedside-remote@hugo2006alm` is also supported. Diagnostics never go to native stdout.

If the extension reports `No such native application com.bedside.remote`, run `./scripts/repair-browser-integration.ps1` from the repository root in your normal Windows PowerShell terminal (5.1 or later). It reports whether the registration already matched, rewrites only the installed host manifest and its current-user registry value, and retains the exact extension allowlist. A custom installation can use `-InstallDirectory`. Run it as the same Windows user as the browser; allow up to 30 seconds for the extension to reconnect. This is a registration repair, not proof of a working browser connection. Record the browser result separately.

Discovery uses `_bedside-remote._tcp` with protocol TXT `version=1`, advertised through the small, dependency-free [Haukcode.Mdns](https://www.nuget.org/packages/Haukcode.Mdns/1.0.21) library. The optional `addresses` TXT field contains up to 16 distinct private IPv4 addresses within one 255-byte DNS-SD string. Android uses these hints with resolved addresses and prefers its own subnet, so a VPN or virtual-adapter address appearing first does not hide the PC. Hints never replace certificate verification or pairing approval. Manual local addresses are shown in the window. If Windows Firewall asks, allow the application on the intended private network. The installer creates no broad firewall exception. Do not forward its port through a router.

## Storage, limits and behavior

- `%LOCALAPPDATA%\BedsideRemote` holds settings and the authoritative YouTube queue. The persistent self-signed TLS identity and device token hashes are encrypted with Windows DPAPI for the current user. A transient current-user key container makes TLS usable with Windows Schannel. Updates preserve this directory; uninstall preserves it as well. Removing it resets identity and all pairings.
- Revocation removes credentials and aborts active sockets. Network messages require a bearer token, TLS, a permitted local address and no browser Origin header. Pairing secrets and tokens are never logged. Strict object/action validation, 64 KiB frames, command-rate limits, increasing per-connection sequences and bounded recent UUID windows reject replays. Clients must never replay commands after reconnecting.
- Only one browser/queue operation is admitted at a time; further operations receive an immediate busy rejection rather than executing later. Each operation has a 20-second total deadline. Pending navigation does not block keyboard, pointer or ordinary media commands, and each desktop-input command is serialized across clients. Disconnect cancels pending work; an operation already delivered to a browser may finish there, so a missing acknowledgement is not a reason to retry automatically.
- Up to eight native browser peers, 32 reported player sessions and 50 queue entries are supported. Snapshot metadata is shortened to keep the full state below 64 KiB. Full queue titles remain persisted. Android selection synchronizes the extension target using native `selectTab`; subsequent popup/browser activation changes update the selected remote session.
- Natural ended transitions advance only the selected, matching YouTube entry, once. Transient removal of the target during a remote navigation does not detach it; manual navigation does. Disconnect/reload alone never advances the queue. The current item remains in the queue. Browser failures and autoplay restrictions are returned through acknowledgements or playback state.
- Home opens Windows Start. Seeking uses an available browser player; other universal media controls use standard Windows media keys. Text uses the focused window's keyboard layout for characters reachable without Ctrl/Alt, including balanced Shift and Caps Lock handling, and paced UTF-16 input for other characters. Release modifier keys before sending text. Delivery stops if the foreground window changes; disconnect/expiry cancels remaining characters. Competing desktop actions receive a busy error rather than running later; browser/queue actions remain responsive. Long text can take time, and a partial failure must not be retried automatically. Windows blocks input to elevated applications and the secure desktop; the agent does not attempt to bypass that protection.

Physical Android control, real Zen/Firefox playback, Netflix DRM, keyboard layouts, Windows Firewall behavior and installation/update on a separate clean machine still require manual validation. Automated browser messages do not establish compatibility with an authenticated streaming session.
