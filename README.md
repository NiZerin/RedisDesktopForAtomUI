# Redis Desktop for AtomUI

用 [AtomUI OSS](https://github.com/AtomUI/AtomUI)（Avalonia / .NET）构建的 Redis 桌面客户端，界面结构对标 [AnotherRedisDesktopManager](https://github.com/qishibo/AnotherRedisDesktopManager)：左侧连接手风琴内浏览 Key，右侧 Tab 打开状态页、Key 详情与 CLI。

当前版本覆盖开发规划中的 **P0–P6**：主窗口、连接管理、SCAN 浏览、String/集合/Stream 编辑、CLI / PubSub / 命令日志、SSL / Sentinel / Cluster / SSH、Viewer 与快捷键。

## 环境

- .NET 8 SDK 或更高
- 本机或局域网 Redis。集成测试默认连接 `192.168.227.5:6379`（可用环境变量 `REDIS_TEST_HOST` / `REDIS_TEST_PORT` 覆盖），或使用 `docker/` 下的拓扑夹具

## 运行

```bash
dotnet run --project src/RedisDesktop.App
```

1. 点击「新建连接」
2. 基础页填写 Host / 端口；可切换 Standalone、Sentinel、Cluster，并按需打开 SSL / SSH 页；可选颜色标记与分组
3. 保存后展开左侧连接（或点标题栏 Home）。连上后在连接展开区内用 DB 下拉切库，并扫描 Key
4. 右侧打开该连接的 Status（INFO）首页。在左侧点 Key，右侧打开独立详情 Tab。String（Text/JSON/Hex/Gzip Viewer）、Hash/List/Set/ZSet 与 Stream 均可分页查看
5. 连接标题栏或右键打开 CLI / Pub/Sub；顶栏日志图标查看命令记录（`Ctrl+N` 新建、`Ctrl+,` 设置、`Ctrl+G` 日志）。关闭 Status 或 Key Tab 不会断开连接；断开请用连接右键「断开」

连接配置与侧栏宽度保存在 `%AppData%/RedisDesktopForAtomUI/`。密码、SSH 口令使用 Windows DPAPI 加密，不会以明文写入 `connections.json`。导入导出的密文仅在同一台 Windows 用户下可解密。设置中可切换中/英界面，以及亮色 / 暗色 / 跟随系统主题。可填写 DB 别名，显示在连接名称旁。

## 打包发布

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。下面生成 **Windows x64 自包含单文件**，对方电脑不用再装 .NET。当前不要加 `PublishTrimmed` / NativeAOT（Avalonia + AtomUI 会裁掉所需程序集）。

在仓库根目录执行：

```bash
dotnet publish src/RedisDesktop.App/RedisDesktop.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false -o artifacts/win-x64
```

PowerShell 等价写法：

```powershell
dotnet publish src/RedisDesktop.App/RedisDesktop.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:CopyOutputSymbolsToPublishDirectory=false `
  -o artifacts/win-x64
```

产物是 `artifacts/win-x64/RedisDesktop.exe`。把该目录打成 zip 后即可作为 GitHub Release 附件：

```powershell
Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/RedisDesktop-win-x64.zip -Force
```

其他平台把 `-r win-x64` 换成对应 RID，并改输出目录：

| 平台 | Runtime |
|------|---------|
| Windows x64 | `win-x64` |
| Windows ARM64 | `win-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux x64 | `linux-x64` |

## 集合编辑

Hash / Set / ZSet 使用 `HSCAN` / `SSCAN` / `ZSCAN` 分页，禁止一次 `HGETALL`。List 使用 `LRANGE`，页脚标明「按索引修改基于当前快照」。Stream 使用 `XRANGE`，删除条目会二次确认。只读连接会禁用写入（GUI 与 CLI 共用同一套写命令表）。Hash field TTL 在探测到 `HEXPIRE` 后才会显示。

String Viewer：自动探测 Text / JSON / Hex / Gzip，失败回退 Text/Hex；过大 Key 仍只预览并禁止整包保存。自定义脚本 Viewer 后置。

## CLI、Pub/Sub 与日志

- 拒绝 `MONITOR`（以及订阅类命令）；订阅请用独立 Pub/Sub Tab
- 输出超过 256KB 会截断，可复制全文
- `AUTH` 详情在日志中脱敏

## 生产连接 FAQ

- **SSL**：填写 CA / 客户端证书路径。默认校验证书；「跳过证书校验」仅用于实验环境。
- **Sentinel**：基础页 Host/Port 填 Sentinel；Sentinel 页填写 MasterName 与 Sentinel 密码。
- **Cluster**：Host 作种子。**请填写节点互相通告的内网 IP**，不要填只在本机有效的 `127.0.0.1`，否则其他节点会连不上。
- **SSH**：先保证 Standalone+SSH 能通。Cluster+SSH 会先经隧道执行 `CLUSTER NODES`，再为每个 master 建本地转发；失败信息按 SSH / 发现 / 多节点连接分层提示。

可选 Docker 夹具：`docker/tls`、`docker/sentinel`、`docker/cluster`、`docker/ssh`。对应拓扑连不上时，相关测试会跳过。

## 测试

```bash
dotnet test
```

Infrastructure 烟测默认连接 `192.168.227.5` 的 db15。连不上则跳过而不是失败。可用 `REDIS_TEST_HOST` 覆盖。


## 许可注意

AtomUI OSS 为 LGPL-3.0。本项目只通过 NuGet 引用官方二进制，不修改控件源码。
