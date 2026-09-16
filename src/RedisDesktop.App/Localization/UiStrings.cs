using System.ComponentModel;

namespace RedisDesktop.App;

public sealed class UiStrings : INotifyPropertyChanged
{
    private string _language = UiLanguages.DefaultCode;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language => _language;

    public bool IsEnglish => UiLanguages.IsEnglish(_language);

    public bool IsChinese => UiLanguages.IsChinese(_language);

    public bool PreferLocalizedFontNames => UiLanguages.PreferLocalizedFontNames(_language);

    public void SetLanguage(string language)
    {
        _language = UiLanguages.Normalize(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public string T(string zh, string en) => IsChinese ? zh : en;

    public string Get(string key, string zh, string en)
    {
        if (string.Equals(_language, UiLanguages.ZhCN, StringComparison.OrdinalIgnoreCase))
        {
            return zh;
        }

        if (string.Equals(_language, UiLanguages.EnUS, StringComparison.OrdinalIgnoreCase))
        {
            return en;
        }

        if (UiCatalog.TryGet(_language, key, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return ConvertPlaceholders(text);
        }

        if (UiCatalog.TryGet(UiLanguages.EnUS, key, out text) && !string.IsNullOrWhiteSpace(text))
        {
            return ConvertPlaceholders(text);
        }

        return en;
    }

    public string NewConnection => Get("new_connection", "新建连接", "New Connection");
    public string Settings => Get("settings", "设置", "Settings");
    public string Log => Get("command_log", "日志", "Log");
    public string SearchConnections => Get("search_connection", "搜索连接", "Search connections");
    public string Connect => T("连接", "Connect");
    public string Disconnect => T("断开", "Disconnect");
    public string OpenCli => Get("redis_console", "打开 CLI", "Open CLI");
    public string OpenPubSub => T("Pub/Sub", "Pub/Sub");
    public string CloseConnection => Get("close_connection", "关闭连接", "Close Connection");
    public string DuplicateConnection => Get("duplicate_connection", "复制连接", "Duplicate Connection");
    public string MarkColor => Get("mark_color", "标记颜色", "Mark Color");
    public string ColorRed => T("红", "Red");
    public string ColorOrange => T("橙", "Orange");
    public string ColorGreen => T("绿", "Green");
    public string ColorBlue => T("蓝", "Blue");
    public string ColorPurple => T("紫", "Purple");
    public string ColorNone => T("无", "None");
    public string More => T("更多", "More");
    public string FlushDb => Get("flushdb", "清空当前 DB", "Flush DB");
    public string CloseToConnection => Get("close_to_connection", "关闭连接会关掉该连接下所有 Tab。确定关闭？", "Closing will close all tabs for this connection. Continue?");
    public string CloseToEditConnection => Get("close_to_edit_connection", "编辑前需要先关闭连接。确定关闭并编辑？", "The connection will be closed before editing. Continue?");
    public string FlushDbPrompt => T("请输入 y 以确认清空当前数据库。", "Type y to confirm flushing the current database.");
    public string Edit => Get("edit", "编辑", "Edit");
    public string Clone => T("克隆", "Clone");
    public string Delete => T("删除", "Delete");
    public string Import => Get("import", "导入连接", "Import");
    public string Export => Get("export", "导出连接", "Export");
    public string MoveToGroup => T("移动到分组", "Move to group");
    public string DarkTheme => Get("dark_mode", "暗色主题", "Dark theme");
    public string FollowSystemTheme => T("跟随系统主题", "Follow system theme");
    public string UseEnglish => T("English UI", "English UI");
    public string ScanCount => T("SCAN COUNT（默认 200）", "SCAN COUNT (default 200)");
    public string UiSettings => Get("ui_settings", "外观", "Appearance");
    public string CommonSettings => Get("common_settings", "通用", "General");
    public string ThemeSelect => Get("theme_select", "主题模式", "Color Theme");
    public string ThemeSystem => Get("theme_system", "跟随系统", "System");
    public string ThemeLight => Get("theme_light", "浅色", "Light");
    public string ThemeDark => Get("theme_dark", "深色", "Dark");
    public string SelectLang => Get("select_lang", "语言", "Language");
    public string PageZoom => Get("page_zoom", "页面缩放", "Page Zoom");
    public string FontFamily => Get("font_family", "字体选择", "Font Family");
    public string FontDefault => T("默认", "Default");
    public string FontFaq => T(
        "列表来自本机已安装字体。下拉项按该字体预览，并标出能显示的文字（中文 / 日文 / 韩文等）。选定后会自动拼接系统里的多语言字体，避免缺字。选「默认」则使用界面自带字体。",
        "The list comes from fonts installed on this computer. Each item is previewed in its own typeface and shows which scripts it covers. Your pick is followed by system fallbacks so Chinese, Japanese, Korean, and other text still render. Choose Default to keep the built-in UI font.");
    public string KeysPerLoading => Get("keys_per_loading", "加载数量", "Load Number");
    public string KeysPerLoadingTip => Get("keys_per_loading_tip", "每次 SCAN 加载的 Key 数量，设置过大可能会影响性能。", "Keys loaded per SCAN. Setting this too large may affect performance.");
    public string ConfigConnections => Get("config_connections", "连接配置", "Connections");
    public string AppName => "Redis Desktop";
    public string PreVersion => Get("pre_version", "当前版本", "Current Version");
    public string LatestVersion => T("最新版本", "Latest Version");
    public string CheckUpdate => T("检查更新", "Check for updates");
    public string CheckingUpdate => T("正在检查更新…", "Checking for updates…");
    public string UpdateCheckFailed => T("无法读取 GitHub 最新版本。", "Could not read the latest GitHub release.");
    public string AlreadyLatest => T("已是最新版本", "You are on the latest version");
    public string UpdateAvailable => T("发现新版本", "Update available");
    public string UpdateNow => T("更新", "Update");
    public string DontRemind => T("不再提示", "Don't remind");
    public string DownloadingUpdate => T("正在下载更新", "Downloading update");
    public string CheckUpdatesOnStartup => T("启动时检查更新", "Check for updates at startup");
    public string Hotkey => Get("hotkey", "快捷键", "Hot Key");
    public string ClearCache => Get("clear_cache", "清除缓存", "Clear Cache");
    public string ClearCacheTip => Get("clear_cache_tip", "当客户端出现问题时，该操作会删除所有连接和配置，用于恢复客户端。", "When the client misbehaves, this deletes all connections and settings to recover it.");
    public string ClearCacheDone => T("已清除缓存", "Cache cleared");
    public string OpenConfigFolder => T("打开配置目录", "Open Config Folder");
    public string GitHub => "GitHub";
    public string HotkeyKey => "Key";
    public string HotkeyDesc => T("说明", "Description");
    public string CommandLog => Get("command_log", "命令日志", "Command log");
    public string OnlyWrite => T("仅写命令", "Only Write");
    public string ClearLogs => Get("clean_up", "清空", "Clear");
    public string EmptyWorkspace => T("从左侧连接一个 Redis 实例", "Connect a Redis instance from the sidebar");
    public string SearchKeys => Get("enter_to_search", "回车搜索", "Enter To Search");
    public string ExactSearch => Get("exact_search", "精确搜索", "Exact Search");
    public string LoadAll => Get("load_all_keys", "加载所有", "Load All");
    public string LoadAllTip => Get("load_all_keys_tip", "一次性加载所有 Key，数量过多时可能导致卡顿，请酌情使用", "Load every key at once. Too many keys may freeze the client.");
    public string LoadMore => Get("load_more_keys", "加载更多", "Load More");
    public string MultipleSelect => Get("multiple_select", "多项选择", "Multiple Select");
    public string OpenNewTab => Get("open_new_tab", "新窗口打开", "Open In New Tab");
    public string LoadCurrentFolder => Get("load_current_folder", "只加载该文件夹", "Load Current Folder");
    public string DeleteFolder => Get("delete_folder", "扫描并删除整个文件夹", "Scan And Delete Folder");
    public string ToggleCheckAll => Get("toggle_check_all", "全选 | 取消全选", "Select All / None");
    public string EmptyKeys => T("暂无数据", "No Data");
    public string ScanDisabled => Get("scan_disabled", "SCAN 命令执行异常（可能已被禁用），无法显示 Key 列表", "SCAN is unavailable (it may be disabled). The key list cannot be shown.");
    public string TreeNodeOverflow => Get(
        "tree_node_overflow",
        "Key 或文件夹过多，仅保留 {0} 个进行展示。如未找到所需 Key，建议使用模糊搜索，或设置分隔符将 Key 分散到文件夹中",
        "Too many keys or folders; only {0} are shown. Narrow MATCH, or set a separator to group keys.");
    public string Copy => Get("copy", "复制", "Copy");
    public string NewKey => Get("add_new_key", "新增Key", "New Key");
    public string KeyName => Get("key_name", "键名", "Key Name");
    public string KeyType => Get("key_type", "类型", "Key Type");
    public string AutoRefresh => Get("auto_refresh", "自动刷新", "Auto Refresh");
    public string Server => Get("server", "服务器", "Server");
    public string Memory => Get("memory", "内存", "Memory");
    public string Stats => Get("stats", "统计", "Stats");
    public string KeyStatistics => Get("key_statistics", "Key 统计", "Key Statistics");
    public string AllRedisInfo => Get("all_redis_info", "全部 Redis Info", "All Redis Info");
    public string Home => T("首页", "Home");
    public string Version => Get("pre_version", "版本", "Version");
    public string RedisVersionLabel => Get("redis_version", "Redis 版本", "Redis Version");
    public string OsLabel => "OS";
    public string Process => Get("process_id", "进程", "Process ID");
    public string UsedMemory => Get("used_memory", "已用内存", "Used Memory");
    public string UsedMemoryPeak => Get("used_memory_peak", "内存峰值", "Used Memory Peak");
    public string UsedMemoryLua => Get("used_memory_lua", "Lua 内存", "Used Memory Lua");
    public string Clients => Get("connected_clients", "客户端", "Connected Clients");
    public string TotalConnections => Get("total_connections_received", "总连接数", "Total Connections");
    public string TotalCommands => Get("total_commands_processed", "总命令数", "Total Commands");
    public string EmptyConnections => T("还没有连接。点上方「新建连接」开始。", "No connections yet. Click New Connection above.");
    public string PickKey => T("在左侧选择一个 Key", "Select a key on the left");
    public string DatabaseAlias => T("DB 别名（可空）", "DB alias (optional)");
    public string GroupName => Get("group_name", "分组名（可空）", "Group name (optional)");
    public string Group => Get("group", "分组", "Group");
    public string SelectGroup => Get("select_group", "选择分组", "Select group");
    public string Host => Get("host", "Host", "Host");
    public string Port => Get("port", "端口", "Port");
    public string Password => Get("password", "密码", "Password");
    public string Username => Get("username", "用户名", "Username");
    public string ConnectionName => Get("connection_name", "连接名", "Connection Name");
    public string Separator => Get("separator", "分隔符", "Separator");
    public string SeparatorTip => Get("separator_tip", "用于把 Key 显示成树。留空则只用平铺列表。", "Used to display keys as a tree. Leave empty to disable tree view.");
    public string SeparatorPlaceholder => T("留空则关闭树形视图", "Empty To Disable Tree View");
    public string AclUsernameHint => T("Redis 6.0+ ACL", "ACL in Redis >= 6.0");
    public string PrivateKey => Get("private_key", "私钥", "Private Key");
    public string PublicKey => Get("public_key", "公钥", "Public Key");
    public string Authority => Get("authority", "CA 证书", "Authority");
    public string RedisNodePassword => Get("redis_node_password", "Redis 节点密码", "Redis Node Password");
    public string MasterGroupName => Get("master_group_name", "Master 组名", "Master Group Name");
    public string SkipCertValidation => T("跳过证书校验（不安全）", "Skip certificate validation (insecure)");
    public string ClusterHint => Get("cluster_faq", "Cluster 的 Host 请填节点通告地址，不要填只在本机有效的 127.0.0.1。", "Use the advertised cluster node address, not a localhost-only 127.0.0.1.");
    public string SentinelHint => Get("sentinel_faq", "Host/Port 填 Sentinel 节点。上方密码为 Sentinel AUTH，节点密码在 Sentinel 区域填写。", "Host/Port are Sentinel nodes. The password above is Sentinel AUTH; the Redis node password is in the Sentinel section.");
    public string ReadonlyHint => Get("connection_readonly", "只读连接会拦截写命令。", "Read-only connections reject write commands.");
    public string EditConnection => Get("edit_connection", "编辑连接", "Edit Connection");
    public string Ok => T("确定", "OK");
    public string ColorTag => T("颜色标记", "Color tag");
    public string ShowPassword => T("显示密码", "Show passwords");
    public string TestConnection => Get("test_connection", "测试连接", "Test connection");
    public string Shortcuts => T("Ctrl+N 新建连接 · Ctrl+, 设置 · Ctrl+G 命令日志", "Ctrl+N New connection · Ctrl+, Settings · Ctrl+G Command log");
    public string HotkeyCloseTab => T("关闭 Tab", "Close Tab");
    public string HotkeyRefreshTab => T("刷新 [Status / Key]", "Refresh [Status / Key]");
    public string HotkeyDeleteKey => T("删除 Key [Key Tab]", "Delete key [Key Tab]");
    public string HotkeySaveKey => T("保存 [Key Tab]", "Save [Key Tab]");
    public string HotkeyClearCli => T("清屏 [CLI Tab]", "Clear [CLI Tab]");
    public string HotkeyOpenNewTab => T("在新 Tab 打开 Key", "Open key in a new tab");
    public string HotkeyTips => T("快捷键说明", "Hotkey tips");
    public string ExportWarning => T(
        "导出会包含本机加密后的密码密文，换电脑后密码无法解密，需要重新填写。",
        "Export includes machine-encrypted secrets. Passwords will not decrypt on another PC.");
    public string Scan => T("扫描", "Scan");
    public string Tree => T("树形", "Tree");
    public string Flat => T("平铺", "Flat");
    public string Cancel => T("取消", "Cancel");
    public string Refresh => Get("refresh_connection", "刷新", "Refresh");
    public string Save => Get("save", "保存", "Save");
    public string Rename => T("重命名", "Rename");
    public string ApplyTtl => T("应用 TTL", "Apply TTL");
    public string Persistent => T("持久", "Persistent");
    public string Status => Get("redis_status", "状态", "Status");
    public string Execute => T("执行", "Execute");
    public string CopyOutput => T("复制输出", "Copy output");
    public string CopyFull => T("复制全文", "Copy full");
    public string Subscribe => T("订阅", "Subscribe");
    public string Unsubscribe => T("退订", "Unsubscribe");
    public string Publish => T("PUBLISH", "PUBLISH");
    public string Pattern => T("Pattern", "Pattern");
    public string PrevPage => T("上一页", "Prev");
    public string NextPage => T("下一页", "Next");
    public string Add => Get("new", "新增", "Add");
    public string AddNewLine => Get("add_new_line", "新增行", "Add New Line");
    public string EditLine => Get("edit_line", "编辑行", "Edit Line");
    public string Field => T("Field", "Field");
    public string Member => T("Member", "Member");
    public string Value => T("Value", "Value");
    public string Score => T("Score", "Score");
    public string SearchInPage => T("搜索", "Search");
    public string DumpRow => Get("dump_to_clipboard", "导出命令", "Dump Command");
    public string NarrowPattern => T("结果过多，已截断。请缩小 MATCH。", "Too many keys, truncated. Narrow MATCH.");
    public string OverwriteConfirm => T("服务器上的值已变化，确定覆盖？", "The value on the server has changed. Overwrite?");
    public string Unsaved => T("未保存的修改", "Unsaved changes");
    public string UnsavedSwitchViewer => T("切换格式将丢弃未保存修改，确定继续？", "Switching format discards unsaved changes. Continue?");
    public string UnsavedRefresh => T("刷新将丢弃未保存修改，确定继续？", "Refresh will discard unsaved changes. Continue?");
    public string UnsavedSwitchKey => T("当前 Key 有未保存修改，确定切换？", "The current key has unsaved changes. Switch anyway?");
    public string TtlDeleteConfirm => Get("ttl_delete", "TTL 为 0 会删除该 Key，确定继续？", "TTL 0 will delete this key. Continue?");
    public string Persist => Get("persist", "持久化（-1）", "Persist (-1)");
    public string DumpCommand => Get("dump_to_clipboard", "导出命令到剪贴板", "Dump command to clipboard");
    public string AutoRefreshTip
    {
        get
        {
            var text = Get("auto_refresh_tip", "自动刷新（2 秒）", "Auto refresh (2s)");
            return text.Contains("{0}", StringComparison.Ordinal) ? string.Format(text, 2) : text;
        }
    }
    public string SearchInfo => T("搜索 Info", "Search info");
    public string InfoDisabled => Get("info_disabled", "INFO 命令不可用（可能已被禁用）", "INFO command is unavailable (it may be disabled).");
    public string Node => "Node";
    public string RenameHint => Get("click_enter_to_rename", "修改后按 Enter 重命名", "Press Enter to rename");
    public string TtlHint => Get("click_enter_to_ttl", "TTL 秒，-1 为持久", "TTL seconds, -1 = persist");
    public string Copied => Get("copy_success", "已复制", "Copied");
    public string Saved => Get("modify_success", "已保存", "Saved");
    public string Deleted => Get("delete_success", "已删除", "Deleted");
    public string TtlUpdated => T("TTL 已更新", "TTL updated");
    public string HexTag => "[Hex]";
    public string DeleteKey => T("删除 Key", "Delete key");
    public string DeleteConnection => Get("del_connection", "删除连接", "Delete connection");
    public string GroupPrompt => T("输入分组名，留空则移出分组。", "Enter a group name. Leave empty to ungroup.");
    public string HeartbeatLost => T("心跳失败", "Heartbeat lost");
    public string Connected => T("已连接", "Connected");
    public string Ready => T("就绪", "Ready");

    private static string ConvertPlaceholders(string text)
        => text.Replace("{num}", "{0}", StringComparison.Ordinal)
            .Replace("{interval}", "{0}", StringComparison.Ordinal)
            .Replace("{db}", "{0}", StringComparison.Ordinal)
            .Replace("{txt}", "{0}", StringComparison.Ordinal)
            .Replace("{key}", "{0}", StringComparison.Ordinal);
}
