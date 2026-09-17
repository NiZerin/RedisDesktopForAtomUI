using System.Globalization;
using System.Runtime.Versioning;
using Avalonia.Media;
using Microsoft.Win32;

namespace RedisDesktop.App;

/// <summary>
/// System UI fonts plus a multilingual fallback stack, so a Latin pick still renders CJK and other scripts.
/// </summary>
public static class UiFontCatalog
{
    public const string MultilingualSample = "Aa 中文 あ 한글";

    /// <summary>
    /// Installed-font names used as fallbacks after the user's pick. Order is refined by UI language.
    /// </summary>
    private static readonly string[] FallbackFamilies =
    [
        "Alibaba Sans",
        "Segoe UI",
        "San Francisco",
        ".AppleSystemUIFont",
        "Inter",
        "Noto Sans",
        "DejaVu Sans",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "PingFang SC",
        "Hiragino Sans GB",
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans SC",
        "WenQuanYi Micro Hei",
        "Noto Sans CJK TC",
        "Noto Sans TC",
        "Microsoft JhengHei UI",
        "Microsoft JhengHei",
        "PingFang TC",
        "PingFang HK",
        "Yu Gothic UI",
        "Yu Gothic",
        "Hiragino Sans",
        "Meiryo",
        "Noto Sans CJK JP",
        "Noto Sans JP",
        "Malgun Gothic",
        "Apple SD Gothic Neo",
        "Noto Sans CJK KR",
        "Noto Sans KR",
        "Noto Naskh Arabic",
        "Noto Sans Arabic",
        "Segoe UI Emoji",
        "Apple Color Emoji",
        "Noto Color Emoji",
        "Arial Unicode MS",
        "Arial",
        "sans-serif"
    ];

    private static readonly Dictionary<string, string> LocalizedFamilyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft YaHei"] = "微软雅黑",
        ["Microsoft YaHei UI"] = "微软雅黑 UI",
        ["Microsoft YaHei UI Light"] = "微软雅黑 UI Light",
        ["SimSun"] = "宋体",
        ["NSimSun"] = "新宋体",
        ["SimHei"] = "黑体",
        ["KaiTi"] = "楷体",
        ["FangSong"] = "仿宋",
        ["DengXian"] = "等线",
        ["YouYuan"] = "幼圆",
        ["LiSu"] = "隶书",
        ["STSong"] = "华文宋体",
        ["STHeiti"] = "华文黑体",
        ["STKaiti"] = "华文楷体",
        ["STFangsong"] = "华文仿宋",
        ["Microsoft JhengHei"] = "微軟正黑體",
        ["Microsoft JhengHei UI"] = "微軟正黑體 UI",
        ["MingLiU"] = "細明體",
        ["PMingLiU"] = "新細明體",
        ["DFKai-SB"] = "標楷體",
        ["PingFang SC"] = "苹方-简",
        ["PingFang TC"] = "蘋方-繁",
        ["PingFang HK"] = "蘋方-港",
        ["Hiragino Sans GB"] = "冬青黑体简体中文",
        ["Yu Gothic"] = "游ゴシック",
        ["Yu Gothic UI"] = "Yu Gothic UI",
        ["Yu Mincho"] = "游明朝",
        ["Meiryo"] = "メイリオ",
        ["Meiryo UI"] = "Meiryo UI",
        ["MS Gothic"] = "ＭＳ ゴシック",
        ["MS Mincho"] = "ＭＳ 明朝",
        ["MS PGothic"] = "ＭＳ Ｐゴシック",
        ["MS PMincho"] = "ＭＳ Ｐ明朝",
        ["Hiragino Sans"] = "ヒラギノ角ゴシック",
        ["Hiragino Mincho ProN"] = "ヒラギノ明朝",
        ["Malgun Gothic"] = "맑은 고딕",
        ["Gulim"] = "굴림",
        ["Dotum"] = "돋움",
        ["Batang"] = "바탕",
        ["Gungsuh"] = "궁서",
        ["Apple SD Gothic Neo"] = "Apple SD 산돌고딕 Neo"
    };

    private static IReadOnlyList<SystemFontInfo>? _systemFonts;
    private static HashSet<string>? _installedNames;

    public static IReadOnlyList<FontChoice> ListChoices(string defaultLabel, bool preferLocalized)
    {
        var list = new List<FontChoice> { FontChoice.CreateDefault(defaultLabel) };
        foreach (var font in GetSystemFonts())
        {
            list.Add(font.ToChoice(preferLocalized));
        }

        return list;
    }

    public static FontFamily? Create(IEnumerable<string>? preferred, string? language = null)
    {
        var stack = BuildStack(preferred, language);
        if (stack.Count == 0)
        {
            return null;
        }

        // Avalonia splits family lists on commas and does NOT treat quotes as grouping.
        // Wrapping "Microsoft YaHei" in quotes made CJK fonts fail to resolve, while
        // unquoted names like Consolas still worked.
        return new FontFamily(string.Join(",", stack));
    }

    public static IReadOnlyList<string> BuildStack(IEnumerable<string>? preferred, string? language = null)
    {
        var stack = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var trimmed = SanitizeFamilyName(name);
            if (trimmed.Length == 0
                || string.Equals(trimmed, "Default Initial", StringComparison.OrdinalIgnoreCase)
                || !seen.Add(trimmed))
            {
                return;
            }

            stack.Add(trimmed);
        }

        foreach (var name in preferred ?? [])
        {
            foreach (var alias in AliasesOf(name))
            {
                TryAdd(alias);
            }
        }

        if (stack.Count == 0)
        {
            return [];
        }

        var installed = GetInstalledNames();
        foreach (var name in OrderFallbacks(language))
        {
            if (string.Equals(name, "Alibaba Sans", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "sans-serif", StringComparison.OrdinalIgnoreCase)
                || installed.Contains(name))
            {
                TryAdd(name);
            }
        }

        return stack;
    }

    public static string SanitizeFamilyName(string name)
        => name.Trim().Trim('"').Replace(",", " ", StringComparison.Ordinal).Trim();

    private static IEnumerable<string> OrderFallbacks(string? language)
    {
        var tag = string.IsNullOrWhiteSpace(language)
            ? CultureInfo.CurrentUICulture.Name
            : language;
        IReadOnlyList<string> leading = tag.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                                         || tag.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                                         || tag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            ? ["Microsoft JhengHei UI", "Microsoft JhengHei", "PingFang TC", "PingFang HK", "Noto Sans CJK TC"]
            : tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                ? ["Yu Gothic UI", "Yu Gothic", "Hiragino Sans", "Meiryo", "Noto Sans CJK JP"]
                : tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
                    ? ["Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR"]
                    : ["Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Hiragino Sans GB", "Noto Sans CJK SC"];

        foreach (var name in leading)
        {
            yield return name;
        }

        foreach (var name in FallbackFamilies)
        {
            yield return name;
        }
    }

    private static IEnumerable<string> AliasesOf(string name)
    {
        var trimmed = SanitizeFamilyName(name);
        if (trimmed.Length == 0)
        {
            yield break;
        }

        yield return trimmed;
        if (LocalizedFamilyNames.TryGetValue(trimmed, out var localized))
        {
            yield return localized;
        }

        foreach (var (english, chinese) in LocalizedFamilyNames)
        {
            if (string.Equals(chinese, trimmed, StringComparison.Ordinal))
            {
                yield return english;
            }
        }
    }

    private static IReadOnlyList<SystemFontInfo> GetSystemFonts()
    {
        EnsureSystemFontsLoaded();
        return _systemFonts!;
    }

    private static HashSet<string> GetInstalledNames()
    {
        EnsureSystemFontsLoaded();
        return _installedNames!;
    }

    private static void EnsureSystemFontsLoaded()
    {
        if (_installedNames is not null && _systemFonts is not null)
        {
            return;
        }

        var byName = new Dictionary<string, SystemFontInfo>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? rawName, FontFamily? preview)
        {
            var name = SanitizeFamilyName(rawName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith('@')
                || name.Contains("avares:", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Default Initial", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            names.Add(name);
            foreach (var alias in AliasesOf(name))
            {
                names.Add(alias);
            }

            if (byName.ContainsKey(name))
            {
                return;
            }

            LocalizedFamilyNames.TryGetValue(name, out var localized);
            if (localized is null)
            {
                foreach (var (english, chinese) in LocalizedFamilyNames)
                {
                    if (string.Equals(chinese, name, StringComparison.Ordinal))
                    {
                        localized = english;
                        break;
                    }
                }
            }

            if (string.Equals(localized, name, StringComparison.Ordinal))
            {
                localized = null;
            }

            byName[name] = new SystemFontInfo(name, localized, preview ?? new FontFamily(name));
        }

        try
        {
            foreach (var family in FontManager.Current.SystemFonts)
            {
                TryAdd(family.Name, family);
            }
        }
        catch
        {
            // Avalonia font enumeration can fail on some platforms.
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var name in EnumerateWindowsFontFamilies())
            {
                TryAdd(name, null);
            }
        }

        var fonts = byName.Values.ToList();
        fonts.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase));
        _systemFonts = fonts;
        _installedNames = names;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> EnumerateWindowsFontFamilies()
    {
        var results = new List<string>();
        CollectWindowsFontKey(Registry.LocalMachine, results);
        CollectWindowsFontKey(Registry.CurrentUser, results);
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void CollectWindowsFontKey(RegistryKey hive, List<string> results)
    {
        try
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var name = StripFontRegistrySuffix(valueName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    results.Add(name);
                }
            }
        }
        catch
        {
            // Registry access can fail in locked-down environments.
        }
    }

    private static string StripFontRegistrySuffix(string valueName)
    {
        var name = valueName.Trim();
        string[] suffixes =
        [
            " (TrueType)",
            " (OpenType)",
            " (TrueType Collection)",
            " (All res)",
            " (VGA res)",
            " (Plotter)",
            " (Vector)",
            " (PostScript)"
        ];
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length].Trim();
                break;
            }
        }

        var amp = name.IndexOf(" & ", StringComparison.Ordinal);
        if (amp > 0)
        {
            name = name[..amp].Trim();
        }

        return name;
    }

    private sealed record SystemFontInfo(string Name, string? LocalizedName, FontFamily Preview)
    {
        public FontChoice ToChoice(bool preferLocalized)
        {
            string label;
            if (string.IsNullOrWhiteSpace(LocalizedName) || string.Equals(LocalizedName, Name, StringComparison.Ordinal))
            {
                label = Name;
            }
            else if (preferLocalized)
            {
                label = $"{LocalizedName} ({Name})";
            }
            else
            {
                label = $"{Name} ({LocalizedName})";
            }

            return new FontChoice(Name, label, Preview);
        }
    }
}

public sealed class FontChoice : IEquatable<FontChoice>
{
    public FontChoice(string name, string label, FontFamily preview, string? sample = null)
    {
        Name = name;
        Label = label;
        Preview = preview;
        Sample = sample ?? UiFontCatalog.MultilingualSample;
    }

    public string Name { get; }

    public string Label { get; }

    public FontFamily Preview { get; }

    public string Sample { get; }

    public bool IsDefault => string.IsNullOrEmpty(Name);

    public static FontChoice CreateDefault(string label)
        => new(string.Empty, label, FontFamily.Default, UiFontCatalog.MultilingualSample);

    public bool Equals(FontChoice? other)
        => other is not null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is FontChoice other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public override string ToString() => IsDefault ? Label : $"{Label} {Name}";
}
