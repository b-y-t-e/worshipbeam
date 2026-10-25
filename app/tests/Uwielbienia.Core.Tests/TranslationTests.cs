using System.Text.Json;
using System.Text.RegularExpressions;
using Uwielbienia.Core.Localization;

namespace Uwielbienia.Core.Tests;

public sealed class TranslationTests
{
    private static readonly string Src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    private static Dictionary<string, string> Dictionary(string language) =>
        Directory.GetFiles(Path.Combine(Src, "Uwielbienia.Core", "Localization", language), "*.json")
            .SelectMany(f => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!)
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value);

    private static readonly Regex Usage = new(@"Tr\.[TF]\(""((?:[^""\\]|\\.)*)""|\{l:T '([^']*)'\}");

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("hr")]
    public void EveryTextInCodeHasTranslation(string language)
    {
        var texts = Dictionary(language);
        var used = Directory.GetFiles(Src, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Usage.Matches(File.ReadAllText(f)))
            .Select(m => Regex.Unescape(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .Distinct();

        var missing = used.Where(t => !texts.ContainsKey(t)).ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("hr")]
    public void TranslationKeepsPlaceholders(string language)
    {
        static string Holes(string s) => string.Join(",", Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).Order());
        var broken = Dictionary(language).Where(p => Holes(p.Key) != Holes(p.Value)).ToList();
        Assert.Empty(broken);
    }

    [Fact]
    public void UsesChosenLanguageAndFallsBackToPolish()
    {
        try
        {
            Tr.Use("en");
            Assert.Equal("Language", Tr.T("Język"));
            Assert.Equal("nieznany tekst", Tr.T("nieznany tekst"));
            Tr.Use("xx");
            Assert.Equal("pl", Tr.Language);
        }
        finally
        {
            Tr.Use(Tr.Polish);
        }
    }
}
