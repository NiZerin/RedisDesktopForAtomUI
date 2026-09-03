# Redis Desktop for AtomUI

<p align="center">
  <img src="screenshots/logo.png" alt="Redis Desktop" width="128" height="128">
</p>

**English** | [简体中文](README-CN.md)

A native Redis desktop client built with [Avalonia](https://avaloniaui.net/) and [AtomUI](https://github.com/AtomUI/AtomUI). The layout follows [Another Redis Desktop Manager](https://github.com/qishibo/AnotherRedisDesktopManager): browse keys in a left-side connection accordion, and open Status, key details, and CLI in tabs on the right. Not Electron.

[![Release](https://img.shields.io/github/v/release/NiZerin/RedisDesktopForAtomUI)](https://github.com/NiZerin/RedisDesktopForAtomUI/releases)
[![Downloads](https://img.shields.io/github/downloads/NiZerin/RedisDesktopForAtomUI/total)](https://github.com/NiZerin/RedisDesktopForAtomUI/releases)
[![Stars](https://img.shields.io/github/stars/NiZerin/RedisDesktopForAtomUI)](https://github.com/NiZerin/RedisDesktopForAtomUI/stargazers)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Platform](https://img.shields.io/badge/Windows-x64-0078D4)

## Screenshots

![Status](screenshots/ScreenShot_2026-08-28_143022_020.png)

![Key details](screenshots/ScreenShot_2026-08-28_143108_925.png)

![Settings](screenshots/ScreenShot_2026-08-28_143213_335.png)

## Download

Get `RedisDesktop-win-x64.zip` from [Releases](https://github.com/NiZerin/RedisDesktopForAtomUI/releases/latest), extract it, and run `RedisDesktop.exe`.

- Self-contained Windows x64 single-file build — **no .NET install required**
- First launch may be slower while native libraries such as Skia are extracted
- Not a NativeAOT or trimmed build, so Avalonia and AtomUI are not stripped

macOS and Linux can be published from source with `dotnet publish` (see below). There is no official installer for those platforms yet.

## Features

- **Connections**: Standalone / Sentinel / Cluster, optional SSL and SSH (including Cluster + SSH tunnels); groups, color tags, DB aliases; test connection and heartbeat
- **Keys**: SCAN by default (not `KEYS *`); tree or flat list; load more / load all; multi-select; open in a new tab
- **Editing**: paginated create/update/delete for String, Hash, List, Set, ZSet, and Stream; TTL; writes disabled on read-only connections
- **Viewer**: Text / JSON / Hex / Gzip / Deflate / Brotli
- **Status**: INFO home (version / memory / clients / keyspace) with optional auto-refresh
- **CLI / Pub/Sub / command log**: history and completion; subscriptions use a dedicated tab; `AUTH` is masked in the log; `PING` is not recorded
- **UI**: 13 languages (same set as Another Redis Desktop Manager); light / dark / follow system; zoom and font; sidebar width is remembered

## Usage

1. Click **New connection**, enter host / port, choose Standalone, Sentinel, or Cluster, and enable SSL / SSH if needed
2. Expand the connection on the left. For non-cluster servers, switch databases from the DB dropdown, then search and scan keys in the connection pane
3. Status opens on the right. Click a key to open a detail tab. Closing a tab **does not** disconnect — use **Disconnect** on the connection context menu
4. Open CLI / Pub/Sub from the connection header or context menu. Open the command log with the clock button or `Ctrl+G`

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New connection |
| `Ctrl+,` | Settings |
| `Ctrl+G` | Command log |
| `Ctrl+/` | Shortcut list |
| `Ctrl+L` | Clear CLI |
| `Ctrl` + click a key | Open in a new tab |

Settings live in `%AppData%\RedisDesktopForAtomUI\`. Passwords and SSH passphrases are encrypted with Windows DPAPI and are not stored as plaintext in `connections.json`. Exported secrets can only be decrypted by the same Windows user.

## Connection notes

- **SSL**: provide CA / client certificates. Certificate validation is on by default; skip it only in lab environments
- **Sentinel**: put the Sentinel host/port on the basic page; set MasterName and the Sentinel password on the Sentinel page
- **Cluster**: the host is a seed. Use addresses that nodes advertise to each other, not a machine-local `127.0.0.1`
- **SSH**: make Standalone + SSH work first. Cluster + SSH runs `CLUSTER NODES` through the tunnel, then opens a local forward per master

## Not included yet

No Slow Log, memory analysis, dedicated RedisJSON / TimeSeries / Vector editors, custom script viewers, DUMP bulk import/export, or auto-update. CLI completion is static. `MONITOR` is rejected; use the Pub/Sub tab for subscriptions.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/NiZerin/RedisDesktopForAtomUI.git
cd RedisDesktopForAtomUI
dotnet run --project src/RedisDesktop.App
```

```bash
dotnet test
```

Integration tests skip instead of failing when Redis is unreachable. Override the target with `REDIS_TEST_HOST` / `REDIS_TEST_PORT`.

### Package it yourself

```bash
dotnet publish src/RedisDesktop.App/RedisDesktop.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false -o artifacts/win-x64
```

Output: `artifacts/win-x64/RedisDesktop.exe`. Do not add `PublishTrimmed` or `PublishAot`. For other platforms, replace `-r win-x64` with `win-arm64`, `osx-arm64`, `osx-x64`, or `linux-x64`.

## Credits and license

The UI flow is modeled on [Another Redis Desktop Manager](https://github.com/qishibo/AnotherRedisDesktopManager); this project does not port its Electron / Vue code. The UI uses [AtomUI OSS](https://github.com/AtomUI/AtomUI) (LGPL-3.0) via official NuGet binaries only — the control library source is not modified.
