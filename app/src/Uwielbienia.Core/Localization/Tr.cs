using System.Globalization;
using System.Text.Json;

namespace Uwielbienia.Core.Localization;

/// <summary>Język interfejsu: kod i nazwa w tym języku (do wyboru w aplikacji).</summary>
public sealed record UiLanguage(string Code, string Name);

/// <summary>
/// Tłumaczenia interfejsu. Kluczem jest tekst polski — brak tłumaczenia pokazuje tekst polski, więc nic się nie psuje.
/// Słowniki: <c>Localization/{kod}/*.json</c> (zasoby wbudowane). Język wybiera się raz przy starcie (<see cref="Use"/>).
/// Treści zapisywane w plikach (nazwy części w <c>piesn.md</c>, nazwy folderów) zostają po polsku.
/// </summary>
public static class Tr
{
    public const string Polish = "pl";

    public static IReadOnlyList<UiLanguage> Languages { get; } =
    [
        new("pl", "Polski"),
        new("en", "English"),
        new("hu", "Magyar"),
        new("hr", "Hrvatski"),
    ];

    private static IReadOnlyDictionary<string, string> _texts = new Dictionary<string, string>();

    public static string Language { get; private set; } = Polish;

    /// <summary>Kultura wybranego języka — daty i liczby w interfejsie.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    /// <summary>Język systemu, jeśli jest wśród dostępnych; inaczej polski.</summary>
    public static string SystemLanguage()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == code) ? code : Polish;
    }

    public static void Use(string? code)
    {
        Language = Languages.Any(l => l.Code == code) ? code! : Polish;
        _texts = Load(Language);
    }

    public static string T(string polish) => _texts.TryGetValue(polish, out var text) && text.Length > 0 ? text : polish;

    public static string F(string polish, params object?[] args) => string.Format(Culture, T(polish), args);

    private static Dictionary<string, string> Load(string code)
    {
        var texts = new Dictionary<string, string>();
        if (code == Polish)
            return texts;
        var assembly = typeof(Tr).Assembly;
        var prefix = $"Localization/{code}/";
        // %(RecursiveDir) w nazwie zasobu ma na Windows „\”
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var part = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            foreach (var (key, value) in part)
                texts[key] = value;
        }
        return texts;
    }
}
