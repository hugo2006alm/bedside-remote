# Third-party notices

These notices apply only to the named third-party components distributed with the Windows agent. They do not grant a license to Bedside Remote's own source code. No project-wide license has been selected.

| Directory | Component and source |
| --- | --- |
| `Haukcode.Mdns` | Haukcode.Mdns 1.0.21, copyright 2026 Hakan Lindestaf. Unmodified MIT license from [the package's exact source commit](https://github.com/HakanL/Haukcode.Mdns/blob/9fe93c85b67949c1c9f8b114e24fe8d10870548c/LICENSE). |
| `dotnet-runtime` | Unmodified `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` from Microsoft's `Microsoft.NETCore.App.Runtime.win-x64` 10.0.13 NuGet runtime pack. |
| `aspnetcore-runtime` | Unmodified `LICENSE.txt` and `THIRD-PARTY-NOTICES.TXT` from Microsoft's `Microsoft.AspNetCore.App.Runtime.win-x64` 10.0.13 NuGet runtime pack. |
| `windowsdesktop-runtime` | Unmodified `LICENSE` from Microsoft's `Microsoft.WindowsDesktop.App.Runtime.win-x64` 10.0.13 NuGet runtime pack, which contains no separate third-party-notices file. |

The self-contained publish and installer preserve this directory. When updating a dependency or the redistributed runtime, refresh the corresponding notices from the actual package or its pinned source commit.
