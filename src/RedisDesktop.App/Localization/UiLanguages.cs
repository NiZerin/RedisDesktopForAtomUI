using AtomUI;
using AtomUI.Localization;
using Avalonia;

namespace RedisDesktop.App;

public sealed record UiLanguage(string Code, string Label);

public static class UiLanguages
{
    public const string ZhCN = "zh-CN";
    public const string EnUS = "en-US";
    public const string ZhTW = "zh-TW";
    public const string TrTR = "tr-TR";
    public const string RuRU = "ru-RU";
    public const string PtBR = "pt-BR";
    public const string DeDE = "de-DE";
    public const string FrFR = "fr-FR";
    public const string UkUA = "uk-UA";
    public const string ItIT = "it-IT";
    public const string EsES = "es-ES";
    public const string KoKR = "ko-KR";
    public const string ViVN = "vi-VN";

    public const string DefaultCode = ZhCN;

    /// <summary>
    /// Same order and labels as ARDM <c>LanguageSelector.vue</c>.
    /// </summary>
    public static readonly IReadOnlyList<UiLanguage> All =
    [
        new(EnUS, "English"),
        new(ZhCN, "简体中文"),
        new(ZhTW, "繁體中文"),
        new(TrTR, "Türkçe"),
        new(RuRU, "Русский"),
        new(PtBR, "Português"),
        new(DeDE, "Deutsch"),
        new(FrFR, "Français"),
        new(UkUA, "Українською"),
        new(ItIT, "Italiano"),
        new(EsES, "Español"),
        new(KoKR, "한국어"),
        new(ViVN, "Tiếng Việt")
    ];

    public static IReadOnlyList<LanguageTag> AtomUiTags { get; } =
    [
        LanguageTags.ZhCN,
        LanguageTags.EnUS,
        LanguageTags.ZhTW
    ];

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultCode;
        }

        var tag = language.Trim();
        if (All.Any(item => string.Equals(item.Code, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return All.First(item => string.Equals(item.Code, tag, StringComparison.OrdinalIgnoreCase)).Code;
        }

        if (tag.Equals("en", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
        {
            return EnUS;
        }

        if (tag.Equals("cn", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-SG", StringComparison.OrdinalIgnoreCase))
        {
            return ZhCN;
        }

        if (tag.Equals("tw", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("zh-Hant-", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-TW", StringComparison.OrdinalIgnoreCase))
        {
            return ZhTW;
        }

        if (tag.Equals("tr", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("tr-", StringComparison.OrdinalIgnoreCase))
        {
            return TrTR;
        }

        if (tag.Equals("ru", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("ru-", StringComparison.OrdinalIgnoreCase))
        {
            return RuRU;
        }

        if (tag.Equals("pt", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("pt-", StringComparison.OrdinalIgnoreCase))
        {
            return PtBR;
        }

        if (tag.Equals("de", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("de-", StringComparison.OrdinalIgnoreCase))
        {
            return DeDE;
        }

        if (tag.Equals("fr", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("fr-", StringComparison.OrdinalIgnoreCase))
        {
            return FrFR;
        }

        if (tag.Equals("ua", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("uk", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("uk-", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("ua-", StringComparison.OrdinalIgnoreCase))
        {
            return UkUA;
        }

        if (tag.Equals("it", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("it-", StringComparison.OrdinalIgnoreCase))
        {
            return ItIT;
        }

        if (tag.Equals("es", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("es-", StringComparison.OrdinalIgnoreCase))
        {
            return EsES;
        }

        if (tag.Equals("ko", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("ko-", StringComparison.OrdinalIgnoreCase))
        {
            return KoKR;
        }

        if (tag.Equals("vi", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("vi-", StringComparison.OrdinalIgnoreCase))
        {
            return ViVN;
        }

        return DefaultCode;
    }

    public static bool IsEnglish(string? language)
        => Normalize(language) == EnUS;

    public static bool IsChinese(string? language)
    {
        var code = Normalize(language);
        return code is ZhCN or ZhTW;
    }

    public static bool PreferLocalizedFontNames(string? language)
    {
        var code = Normalize(language);
        return code is ZhCN or ZhTW or KoKR;
    }

    public static LanguageTag ToAtomUiTag(string? language)
    {
        return Normalize(language) switch
        {
            ZhCN => LanguageTags.ZhCN,
            ZhTW => LanguageTags.ZhTW,
            _ => LanguageTags.EnUS
        };
    }

    public static void ApplyAtomUi(string? language)
    {
        var manager = Application.Current?.GetLanguageManager();
        if (manager is null)
        {
            return;
        }

        var tag = ToAtomUiTag(language);
        if (manager.Current.CurrentLanguage == tag)
        {
            return;
        }

        try
        {
            manager.ChangeLanguage(tag);
        }
        catch (LanguageNotSupportedException)
        {
            // AtomUI 6.1.3 ships en-US / zh-CN / zh-TW only.
        }
    }
}
