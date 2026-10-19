# Bedside Remote protocol v1

This file is the shared implementation contract. All components must follow these names and shapes. JSON uses camelCase. Unknown versions, types, actions, keys and invalid payloads must be rejected.

## Network and pairing

- Windows advertises `_bedside-remote._tcp` using DNS-SD/mDNS. Default HTTPS port: 8765. TXT `version=1`. Discovery supplies an address, never trust.
- Windows owns a persistent self-signed TLS certificate. Its SHA-256 DER fingerprint (64 hex digits) is shown on the Windows pairing screen. Android requires the user to enter/verify that fingerprint from Windows before sending any pairing secret. Pin matching is mandatory for both HTTPS and WSS; do not install a trust-all fallback.
- Windows opens a five-minute pairing window showing a cryptographically random six-digit code. `POST /pair` accepts `{version:1,deviceName:string,code:string}` only during this window. Rate-limit attempts. Windows must explicitly approve each new device in its local UI.
- Response: `{version:1,deviceId:string,token:string,deviceName:string}`. Token is random 256-bit, stored with Windows DPAPI and Android Keystore encryption. Fingerprint/address are retained per computer.
- `GET /ws` upgrades to WSS with `Authorization: Bearer <token>`. Reject browser Origin headers, public remote addresses, invalid/revoked tokens and oversized messages (64 KiB). No credentials in URLs or logs. Close active connections on revocation and agent exit. Ping every 20 seconds, with a 15-second pong deadline. A rate-limit close uses 1013 (retry later), not a revocation signal. Only HTTP 401 during authentication establishes revoked/invalid credentials; a legacy 1008 close is retryable.
- `GET /health` returns only `{version:1,name:string}`. No control is possible without authentication. LAN includes RFC1918 IPv4, loopback, IPv6 link-local and ULA only.

## Android to Windows

Every command: `{version:1,type:"command",id:<UUID>,sequence:<positive integer>,action:string,payload:object}`. Sequence strictly increases within each authenticated connection. No command replay after reconnect. Server replies `{version:1,type:"ack",id:string,ok:boolean,error?:string}`. Acknowledgement means command accepted/executed, not proof of browser autoplay success.

Universal input remains responsive while browser work is pending. Admit only one slow browser operation at a time; reject additional work promptly with a busy error instead of executing stale queued commands. Native result timeout is 20 seconds; Android waits 25 seconds for browser/queue acknowledgements and 10 seconds for ordinary input. Text delivery is paced and runs separately from socket reads, with a 300-second server deadline and a 315-second Android acknowledgement deadline for the maximum 4096 UTF-16 units. While text is being delivered, reject competing desktop input promptly instead of queueing it; browser/queue operations remain available. Stop text on foreground-window changes, disconnect or expiry. Some text may already have been entered when an error occurs; never automatically replay it.

Android retains text/link drafts until a successful acknowledgement, clears pending state on rejection/disconnect and never resends automatically. Browser-session selection remains available even when the previous target disappears and playback is null. Persist the queue only when queue contents/current entry change; progress-only snapshots do not rewrite storage.

| Action | Payload |
| --- | --- |
| `input.key` | `{key:"up"|"down"|"left"|"right"|"enter"|"escape"|"home"|"backspace"|"tab"|"space"}`; Home means Windows Start menu. |
| `input.text` | `{text:string}`; 1–4096 UTF-16 code units; use Unicode input. |
| `input.pointer` | `{dx:number,dy:number}`; finite, each between -1000 and 1000. |
| `input.click` | `{button:"left"|"right"}` |
| `input.scroll` | `{delta:integer}`; -1200..1200, Windows wheel units. |
| `media.command` | `{command:"playPause"|"next"|"previous"|"volumeUp"|"volumeDown"|"mute"|"seekForward"|"seekBackward"}` |
| `browser.command` | `{command:"playPause"|"seek"|"skipIntro"|"skipRecap"|"nextEpisode"|"fullscreen"|"subtitles"|"audio",position?:number}`; `seek` requires finite nonnegative seconds. |
| `browser.select` | `{sessionId:string}`; must exist in current state. |
| `youtube.enqueue` | `{url:string,mode:"now"|"next"|"last"}`; HTTPS official YouTube links only; canonical video ID matches `[A-Za-z0-9_-]{11}`. |
| `queue.remove` | `{entryId:string}` |
| `queue.move` | `{entryId:string,index:integer}`; zero-based within current queue. |
| `queue.clear` / `queue.next` / `queue.previous` | `{}` |

Allow official hosts `youtube.com`, `www.youtube.com`, `m.youtube.com`, `music.youtube.com` and `youtu.be` only; paths `/watch?v=`, `/shorts/`, `/live/`, `/embed/`, or `youtu.be/<id>`. Reject credentials, nondefault ports, spoofed hosts, playlists without a video and malformed IDs. Canonical URL is `https://www.youtube.com/watch?v=<id>`.

## Windows to Android

Send a full snapshot immediately after connection and after changes:

```json
{"version":1,"type":"state","deviceName":"Bedroom PC","extensionConnected":false,"activeSessionId":null,"sessions":[],"playback":null,"queue":[],"currentEntryId":null}
```

- Queue item: `{entryId:string,videoId:string,url:string,title:string}`. Windows is the persistent source of truth. Keep the currently playing entry in the queue and point to it with `currentEntryId`.
- Session: `{sessionId:string,tabId:number,platform:"youtube"|"netflix",title:string}`. `sessionId` is agent-assigned `<native connection ID>:<tab ID>`.
- Playback: `{platform:"youtube"|"netflix",tabId:number,videoId?:string,title:string,channel?:string,thumbnail?:string,paused:boolean,position:number,duration:number,ended:boolean,capabilities:string[],error?:string}`. Finite position/duration; duration 0 means unavailable/live. Capability strings use browser command names.
- Only a natural `ended` transition for the active YouTube video matching `currentEntryId` can automatically advance once. Manual navigation detaches the current queue entry; closing/reloading a tab or losing the extension must not advance. Preserve queue across disconnects and restarts. Surface autoplay restrictions in playback error/state.

## Native Messaging

Host name: `com.bedside.remote`; extension ID: `bedside-remote@hugo2006alm`. Windows registry host manifest under HKCU Mozilla NativeMessagingHosts. The agent executable supports `--native` and validates Firefox's extension ID argument before bridging framed JSON to a current-user-only named pipe. Browser frames use 4-byte little-endian UTF-8 byte length; limit 64 KiB. Never write diagnostics to stdout. Support multiple browser connections.

The Edge companion uses a separate manifest registered under HKCU `Software\Microsoft\Edge\NativeMessagingHosts\com.bedside.remote`. Its only allowed origin is `chrome-extension://lggdbmfkngfahhnmmibhmefgfgkgngkf/`, fixed by the public key in `browser-extension/config/edge-identity.json`. Edge supplies that exact origin and optionally `--parent-window=<unsigned decimal handle>`; reject other origins or extra arguments. The bridge and wire format remain protocol v1. Store-assigned identity changes require an explicit allowlist/registration migration, never a wildcard.

Extension to host: `{version:1,type:"browser.state",activeTabId?:number,tabs:[<playback objects>]}`. At most 32 tabs per snapshot, with the selected tab first, also respecting the 64 KiB frame bound. Send when tabs/player state changes and at most once per second for progress. Also send `{version:1,type:"browser.result",id:string,ok:boolean,error?:string}` for results.

Host to extension: `{version:1,type:"browser.command",id:string,tabId?:number,command:string,position?:number,url?:string}`. Commands are the browser command names above plus `openYoutube` (validated canonical YouTube URL; select suitable tab or create one) and `selectTab` (required compatible tabId, no URL/position; synchronize the remote target without invoking a player action). Android `browser.select` must synchronize this extension target before acknowledging success; actual browser/popup selection changes also update the agent target. If tabId is absent for a player command, select the active compatible tab. Avoid fixed mouse coordinates and private streaming APIs. A disconnected native host must reconnect with bounded backoff.

After connecting to the interactive agent's pipe, the native executable sends the optional host-to-extension notice `{version:1,type:"native.ready"}` with no additional keys. Creating a browser Port does not establish connectivity: the popup remains connecting until this notice or a valid command arrives, and reports an unconfirmed connection after five seconds. A valid command also confirms older v1 hosts; state publishing remains enabled while confirmation is pending. Older extensions ignore the unknown notice. Manual retry replaces the old port; callbacks from superseded ports cannot change current status.

Incompatible protocol changes require a new major protocol version. Application releases share one SemVer; protocol v1 components remain compatible across application patch/minor releases.
