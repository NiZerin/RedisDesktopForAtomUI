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

    public string T(string zh, string en)
        => string.Equals(_language, UiLanguages.ZhCN, StringComparison.OrdinalIgnoreCase) ? zh : en;

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

    public string Format(string key, string zh, string en, params object[] args)
    {
        var text = Get(key, zh, en);
        if (args.Length == 0)
        {
            return text;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    public string NewConnection => Get("new_connection", "新建连接", "New Connection");
    public string Settings => Get("settings", "设置", "Settings");
    public string Log => Get("command_log", "日志", "Log");
    public string SearchConnections => Get("search_connection", "搜索连接", "Search connections");
    public string Connect => Get("connect", "连接", "Connect");
    public string Disconnect => Get("disconnect", "断开", "Disconnect");
    public string OpenCli => Get("redis_console", "打开 CLI", "Open CLI");
    public string OpenPubSub => Get("pubsub", "Pub/Sub", "Pub/Sub");
    public string MemoryAnalysis => Get("memory_analysis", "内存分析", "Memory Analysis");
    public string SlowLog => Get("slow_log", "慢查询", "Slow Query");
    public string Benchmark => Get("benchmark", "压力测试", "Benchmark");
    public string BenchmarkHint => Get("benchmark_hint",
        "会向服务器写入/读取测试 Key，使用独立连接，不占用当前浏览会话。第一次建议只跑 PING 且不超过 1000 次。",
        "Writes/reads test keys on a dedicated connection, not the browse session. First run should be PING with at most 1000 requests.");
    public string BenchmarkConfirm => Get("benchmark_confirm",
        "将向 Redis 发送压测命令，并可能写入前缀为 __rdbench: 的测试 Key。确定开始？",
        "This will send benchmark commands to Redis and may write keys prefixed with __rdbench:. Start?");
    public string BenchmarkCleanup => Get("benchmark_cleanup", "删除测试 Key", "Delete test keys");
    public string BenchmarkCleanupConfirm => Get("benchmark_cleanup_confirm",
        "将 SCAN 并删除此前缀下的测试 Key。确定继续？",
        "This will SCAN and delete test keys under this prefix. Continue?");
    public string BenchmarkRequests => Get("benchmark_requests", "请求数", "Requests");
    public string BenchmarkConcurrency => Get("benchmark_concurrency", "并发连接", "Clients");
    public string BenchmarkPipeline => Get("benchmark_pipeline", "Pipeline", "Pipeline");
    public string BenchmarkValueSize => Get("benchmark_value_size", "Value 字节", "Value bytes");
    public string BenchmarkDurationMode => Get("benchmark_duration_mode", "按时长运行", "Run for duration");
    public string BenchmarkQps => Get("benchmark_qps", "实时 QPS", "Live QPS");
    public string BenchmarkLatency => Get("benchmark_latency", "延迟分布 (ms)", "Latency (ms)");
    public string BenchmarkIdle => Get("benchmark_idle", "未开始", "Idle");
    public string BenchmarkRunning => Get("benchmark_running", "压测进行中", "Benchmark running");
    public string BenchmarkCancelled => Get("benchmark_cancelled", "已取消", "Cancelled");
    public string BenchmarkClusterHint => Get("benchmark_cluster_hint",
        "Cluster 会按 Key 槽位散列到各节点；测试 Key 使用独立前缀。",
        "Cluster hashes test keys across slots. Keys use a dedicated prefix.");
    public string Stop => Get("stop", "停止", "Stop");
    public string Seconds => Get("seconds", "秒", "sec");
    public string Begin => Get("begin", "开始", "Begin");
    public string Pause => Get("pause", "暂停", "Pause");
    public string Restart => Get("restart", "重新开始", "Restart");
    public string NoSlowLog => Get("no_slow_log", "没有慢日志", "No Slow Log");
    public string SlowLogHint => Get("slow_log_hint",
        "通过 SLOWLOG GET，阈值：CONFIG GET slowlog-log-slower-than，条数：CONFIG GET slowlog-max-len。单位 μs，1000μs = 1ms。",
        "Via SLOWLOG GET. Threshold: CONFIG GET slowlog-log-slower-than. Length: CONFIG GET slowlog-max-len. Unit: μs, 1000μs = 1ms.");
    public string SlowLogTime => Get("slow_log_time", "时间", "Time");
    public string SlowLogSource => Get("slow_log_source", "来源", "Source");
    public string Cost => Get("cost", "耗时", "Cost");
    public string Command => Get("command", "命令", "Command");
    public string SizeLabel => Get("size", "Size", "Size");
    public string MinSizeKb => Get("min_size_kb", "最小 Size (KB)", "Min Size (KB)");
    public string MemoryAnalysisHint => Get("memory_analysis_hint",
        "会 SCAN 并调用 MEMORY USAGE，可能影响延迟。结果为 0 时 MEMORY 可能已被禁用。",
        "This runs SCAN and MEMORY USAGE and may affect latency. Size 0 usually means MEMORY is disabled.");
    public string AnalyzeFolder => Get("analyze_folder", "分析该前缀", "Analyze This Prefix");
    public string MaxDisplay => Get("max_display", "最大显示数量: {0}", "Maximum number of displays: {0}");
    public string MaxScan => Get("max_scan", "最大扫描数量: {0}", "Maximum number of scans: {0}");
    public string CloseConnection => Get("close_connection", "关闭连接", "Close Connection");
    public string DuplicateConnection => Get("duplicate_connection", "复制连接", "Duplicate Connection");
    public string MarkColor => Get("mark_color", "标记颜色", "Mark Color");
    public string ColorRed => Get("color_red", "红", "Red");
    public string ColorOrange => Get("color_orange", "橙", "Orange");
    public string ColorGreen => Get("color_green", "绿", "Green");
    public string ColorBlue => Get("color_blue", "蓝", "Blue");
    public string ColorPurple => Get("color_purple", "紫", "Purple");
    public string ColorNone => Get("color_none", "无", "None");
    public string More => Get("more", "更多", "More");
    public string FlushDb => Get("flushdb", "清空当前 DB", "Flush DB");
    public string CloseToConnection => Get("close_to_connection", "关闭连接会关掉该连接下所有 Tab。确定关闭？", "Closing will close all tabs for this connection. Continue?");
    public string CloseToEditConnection => Get("close_to_edit_connection", "编辑前需要先关闭连接。确定关闭并编辑？", "The connection will be closed before editing. Continue?");
    public string FlushDbPrompt => Get("flushdb_type_y", "请输入 y 以确认清空当前数据库。", "Type y to confirm flushing the current database.");
    public string Edit => Get("edit", "编辑", "Edit");
    public string Clone => Get("clone", "克隆", "Clone");
    public string Delete => Get("delete", "删除", "Delete");
    public string Import => Get("import", "导入连接", "Import");
    public string Export => Get("export", "导出连接", "Export");
    public string MoveToGroup => Get("move_to_group", "移动到分组", "Move to group");
    public string DarkTheme => Get("dark_mode", "暗色主题", "Dark theme");
    public string FollowSystemTheme => Get("theme_system", "跟随系统主题", "Follow system theme");
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
    public string FontDefault => Get("font_default", "默认", "Default");
    public string FontFaq => Get("font_picker_tip",
        "列表为本机已安装的全部字体。下拉项按该字体预览。选定后会自动拼接系统多语言回退字体，避免缺字。选「默认」则使用界面自带字体。",
        "The list is every font installed on this computer. Each item is previewed in its own typeface. Your pick is followed by system fallbacks so other scripts still render. Choose Default to keep the built-in UI font.");
    public string KeysPerLoading => Get("keys_per_loading", "加载数量", "Load Number");
    public string KeysPerLoadingTip => Get("keys_per_loading_tip", "每次 SCAN 加载的 Key 数量，设置过大可能会影响性能。", "Keys loaded per SCAN. Setting this too large may affect performance.");
    public string ConfigConnections => Get("config_connections", "连接配置", "Connections");
    public string AppName => "Redis Desktop";
    public string PreVersion => Get("pre_version", "当前版本", "Current Version");
    public string LatestVersion => Get("latest_version", "最新版本", "Latest Version");
    public string CheckUpdate => Get("check_update", "检查更新", "Check for updates");
    public string CheckingUpdate => Get("update_checking", "正在检查更新…", "Checking for updates…");
    public string UpdateCheckFailed => Get("update_check_failed", "无法读取 GitHub 最新版本。", "Could not read the latest GitHub release.");
    public string AlreadyLatest => Get("update_not_available", "已是最新版本", "You are on the latest version");
    public string UpdateAvailable => Get("update_available", "发现新版本", "Update available");
    public string UpdateNow => Get("begin_update", "更新", "Update");
    public string DontRemind => Get("dont_remind", "不再提示", "Don't remind");
    public string DownloadingUpdate => Get("update_downloading", "正在下载更新", "Downloading update");
    public string CheckUpdatesOnStartup => Get("check_updates_on_startup", "启动时检查更新", "Check for updates at startup");
    public string Hotkey => Get("hotkey", "快捷键", "Hot Key");
    public string ClearCache => Get("clear_cache", "清除缓存", "Clear Cache");
    public string ClearCacheTip => Get("clear_cache_tip", "当客户端出现问题时，该操作会删除所有连接和配置，用于恢复客户端。", "When the client misbehaves, this deletes all connections and settings to recover it.");
    public string ClearCacheDone => Get("clear_cache_done", "已清除缓存", "Cache cleared");
    public string OpenConfigFolder => Get("open_config_folder", "打开配置目录", "Open Config Folder");
    public string GitHub => "GitHub";
    public string HotkeyKey => "Key";
    public string HotkeyDesc => Get("hotkey_desc", "说明", "Description");
    public string CommandLog => Get("command_log", "命令日志", "Command log");
    public string OnlyWrite => Get("only_write", "仅写命令", "Only Write");
    public string ClearLogs => Get("clean_up", "清空", "Clear");
    public string EmptyWorkspace => Get("empty_workspace", "从左侧连接一个 Redis 实例", "Connect a Redis instance from the sidebar");
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
    public string EmptyKeys => Get("empty_keys", "暂无数据", "No Data");
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
    public string Home => Get("home", "首页", "Home");
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
    public string EmptyConnections => Get("empty_connections", "还没有连接。点上方「新建连接」开始。", "No connections yet. Click New Connection above.");
    public string PickKey => Get("pick_key", "在左侧选择一个 Key", "Select a key on the left");
    public string DatabaseAlias => Get("database_alias", "DB 别名（可空）", "DB alias (optional)");
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
    public string SeparatorPlaceholder => Get("separator_placeholder", "留空则关闭树形视图", "Empty To Disable Tree View");
    public string AclUsernameHint => Get("acl_username_hint", "Redis 6.0+ ACL", "ACL in Redis >= 6.0");
    public string PrivateKey => Get("private_key", "私钥", "Private Key");
    public string PublicKey => Get("public_key", "公钥", "Public Key");
    public string Authority => Get("authority", "CA 证书", "Authority");
    public string RedisNodePassword => Get("redis_node_password", "Redis 节点密码", "Redis Node Password");
    public string MasterGroupName => Get("master_group_name", "Master 组名", "Master Group Name");
    public string SkipCertValidation => Get("skip_cert_validation", "跳过证书校验（不安全）", "Skip certificate validation (insecure)");
    public string ClusterHint => Get("cluster_faq", "Cluster 的 Host 请填节点通告地址，不要填只在本机有效的 127.0.0.1。", "Use the advertised cluster node address, not a localhost-only 127.0.0.1.");
    public string SentinelHint => Get("sentinel_faq", "Host/Port 填 Sentinel 节点。上方密码为 Sentinel AUTH，节点密码在 Sentinel 区域填写。", "Host/Port are Sentinel nodes. The password above is Sentinel AUTH; the Redis node password is in the Sentinel section.");
    public string ReadonlyHint => Get("connection_readonly", "只读连接会拦截写命令。", "Read-only connections reject write commands.");
    public string EditConnection => Get("edit_connection", "编辑连接", "Edit Connection");
    public string Ok => Get("ok", "确定", "OK");
    public string ColorTag => Get("color_tag", "颜色标记", "Color tag");
    public string ShowPassword => Get("show_password", "显示密码", "Show passwords");
    public string TestConnection => Get("test_connection", "测试连接", "Test connection");
    public string Shortcuts => Get("shortcuts", "Ctrl+N 新建连接 · Ctrl+, 设置 · Ctrl+G 命令日志", "Ctrl+N New connection · Ctrl+, Settings · Ctrl+G Command log");
    public string HotkeyCloseTab => Get("hotkey_close_tab", "关闭 Tab", "Close Tab");
    public string HotkeyRefreshTab => Get("hotkey_refresh_tab", "刷新 [Status / Key / Slow Log / 内存分析]", "Refresh [Status / Key / Slow Log / Memory]");
    public string HotkeyDeleteKey => Get("hotkey_delete_key", "删除 Key [Key Tab]", "Delete key [Key Tab]");
    public string HotkeySaveKey => Get("hotkey_save_key", "保存 [Key Tab]", "Save [Key Tab]");
    public string HotkeyClearCli => Get("hotkey_clear_cli", "清屏 [CLI Tab]", "Clear [CLI Tab]");
    public string HotkeyOpenNewTab => Get("hotkey_open_new_tab", "在新 Tab 打开 Key", "Open key in a new tab");
    public string HotkeyTips => Get("hotkey_tips", "快捷键说明", "Hotkey tips");
    public string ExportWarning => Get("export_warning",
        "导出会包含本机加密后的密码密文，换电脑后密码无法解密，需要重新填写。",
        "Export includes machine-encrypted secrets. Passwords will not decrypt on another PC.");
    public string Scan => Get("scan", "扫描", "Scan");
    public string Tree => Get("tree", "树形", "Tree");
    public string Flat => Get("flat", "平铺", "Flat");
    public string Cancel => Get("cancel", "取消", "Cancel");
    public string Refresh => Get("refresh_connection", "刷新", "Refresh");
    public string Save => Get("save", "保存", "Save");
    public string Rename => Get("rename", "重命名", "Rename");
    public string ApplyTtl => Get("apply_ttl", "应用 TTL", "Apply TTL");
    public string Persistent => Get("persistent", "持久", "Persistent");
    public string Status => Get("redis_status", "状态", "Status");
    public string Execute => Get("execute", "执行", "Execute");
    public string CopyOutput => Get("copy_output", "复制输出", "Copy output");
    public string CopyFull => Get("copy_full", "复制全文", "Copy full");
    public string Subscribe => Get("subscribe", "订阅", "Subscribe");
    public string Unsubscribe => Get("unsubscribe", "退订", "Unsubscribe");
    public string Publish => Get("publish", "PUBLISH", "PUBLISH");
    public string Pattern => Get("pattern", "Pattern", "Pattern");
    public string PrevPage => Get("prev_page", "上一页", "Prev");
    public string NextPage => Get("next_page", "下一页", "Next");
    public string Add => Get("new", "新增", "Add");
    public string AddNewLine => Get("add_new_line", "新增行", "Add New Line");
    public string EditLine => Get("edit_line", "编辑行", "Edit Line");
    public string Field => Get("field", "Field", "Field");
    public string Member => Get("member", "Member", "Member");
    public string Value => Get("value", "Value", "Value");
    public string Score => Get("score", "Score", "Score");
    public string SearchInPage => Get("search_in_page", "搜索", "Search");
    public string DumpRow => Get("dump_to_clipboard", "导出命令", "Dump Command");
    public string NarrowPattern => Get("narrow_pattern", "结果过多，已截断。请缩小 MATCH。", "Too many keys, truncated. Narrow MATCH.");
    public string OverwriteConfirm => Get("overwrite_confirm", "服务器上的值已变化，确定覆盖？", "The value on the server has changed. Overwrite?");
    public string Unsaved => Get("unsaved", "未保存的修改", "Unsaved changes");
    public string UnsavedSwitchViewer => Get("unsaved_switch_viewer", "切换格式将丢弃未保存修改，确定继续？", "Switching format discards unsaved changes. Continue?");
    public string UnsavedRefresh => Get("unsaved_refresh", "刷新将丢弃未保存修改，确定继续？", "Refresh will discard unsaved changes. Continue?");
    public string UnsavedSwitchKey => Get("unsaved_switch_key", "当前 Key 有未保存修改，确定切换？", "The current key has unsaved changes. Switch anyway?");
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
    public string SearchInfo => Get("search_info", "搜索 Info", "Search info");
    public string InfoDisabled => Get("info_disabled", "INFO 命令不可用（可能已被禁用）", "INFO command is unavailable (it may be disabled).");
    public string Node => "Node";
    public string RenameHint => Get("click_enter_to_rename", "修改后按 Enter 重命名", "Press Enter to rename");
    public string TtlHint => Get("click_enter_to_ttl", "TTL 秒，-1 为持久", "TTL seconds, -1 = persist");
    public string Copied => Get("copy_success", "已复制", "Copied");
    public string Saved => Get("modify_success", "已保存", "Saved");
    public string Deleted => Get("delete_success", "已删除", "Deleted");
    public string TtlUpdated => Get("ttl_updated", "TTL 已更新", "TTL updated");
    public string HexTag => "[Hex]";
    public string DeleteKey => Get("delete_key", "删除 Key", "Delete key");
    public string DeleteConnection => Get("del_connection", "删除连接", "Delete connection");
    public string GroupPrompt => Get("group_prompt", "输入分组名，留空则移出分组。", "Enter a group name. Leave empty to ungroup.");
    public string HeartbeatLost => Get("heartbeat_lost", "心跳失败", "Heartbeat lost");
    public string Connected => Get("connected", "已连接", "Connected");
    public string Ready => Get("ready", "就绪", "Ready");

    private static string ConvertPlaceholders(string text)
        => text.Replace("{num}", "{0}", StringComparison.Ordinal)
            .Replace("{interval}", "{0}", StringComparison.Ordinal)
            .Replace("{db}", "{0}", StringComparison.Ordinal)
            .Replace("{txt}", "{0}", StringComparison.Ordinal)
            .Replace("{key}", "{0}", StringComparison.Ordinal);
}
