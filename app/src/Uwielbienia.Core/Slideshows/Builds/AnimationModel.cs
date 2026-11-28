// Port DeckCraft: crates/model/src/anim.rs (Animation, AnimClass, AnimStart, TextBuild, BuildRole, paragraph_build_roles) — patrz UPSTREAM.md.
using System.Text.Json;

namespace Uwielbienia.Core.Slideshows.Builds;

public enum AnimClass { Entrance, Exit, Emphasis, Path, Media }

public enum AnimStart { OnClick, WithPrevious, AfterPrevious }

/// <summary>Budowanie tekstu: jako całość, akapitami, słowami, literami.</summary>
public enum TextBuild { AsOne, ByParagraph, ByWord, ByLetter }

/// <summary>
/// Animacja z listy w okienku animacji (kolejność = kolejność listy). <see cref="Repeat"/> 0/1 = raz,
/// <see cref="uint.MaxValue"/> = do następnego kliknięcia; <see cref="Path"/> — ścieżka ruchu w ułamkach slajdu
/// (<c>M 0 0 L 0.25 0 E</c>); <see cref="Trigger"/> — kształt, którego kliknięcie ją uruchamia; <see cref="After"/> —
/// <c>hide</c>, <c>hideOnNextClick</c> albo kolor przyciemnienia <c>#RRGGBB</c>.
/// </summary>
public sealed record Animation(int Shape, AnimClass Class, string Effect, string Option, AnimStart Start,
    uint DurationMs, uint DelayMs, uint Repeat, bool AutoReverse, bool Rewind, double SmoothStart, double SmoothEnd,
    double BounceEnd, double? Amount, Rgba? Color, string? Path, int? Paragraph, TextBuild TextBuild, int? Trigger, string? After)
{
    /// <summary>Wartości domyślne jak <c>Animation::default()</c> w Rust (przenikanie po kliknięciu, 500 ms).</summary>
    public static Animation Default(int shape) => new(shape, AnimClass.Entrance, "fade", "", AnimStart.OnClick, 500, 0, 1, false,
        false, 0.0, 0.0, 0.0, null, null, null, null, TextBuild.AsOne, null, null);

    /// <summary>
    /// Jeden element tablicy <c>animations</c> wypisanej przez <c>deckcraft-cli</c> (<c>slide.inspect</c> — serde
    /// <c>Animation</c>, camelCase). Brakujące pola mają wartości domyślne. <c>color</c> może być napisem <c>#RRGGBB</c> albo
    /// obiektem <c>ColorRef</c> (<c>{"base":{"kind":"rgb","rgb":{"r":…}}}</c>; kolor motywu — wg domyślnego schematu DeckCraft).
    /// </summary>
    public static Animation FromJson(JsonElement e)
    {
        string? Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double? Num(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        bool Bool(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        uint UInt(string name, uint def) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var u) ? u : def;
        int? Int(string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) && l is >= 0 and <= int.MaxValue
                ? (int)l
                : null;
        var d = Default(Int("shape") ?? 0);
        return d with
        {
            Class = Str("class") switch
            {
                "emphasis" => AnimClass.Emphasis,
                "exit" => AnimClass.Exit,
                "path" => AnimClass.Path,
                "media" => AnimClass.Media,
                _ => AnimClass.Entrance,
            },
            Effect = Str("effect") ?? d.Effect,
            Option = Str("option") ?? "",
            Start = Str("start") switch
            {
                "withPrevious" => AnimStart.WithPrevious,
                "afterPrevious" => AnimStart.AfterPrevious,
                _ => AnimStart.OnClick,
            },
            DurationMs = UInt("durationMs", d.DurationMs),
            DelayMs = UInt("delayMs", 0),
            Repeat = UInt("repeat", 1),
            AutoReverse = Bool("autoReverse"),
            Rewind = Bool("rewind"),
            SmoothStart = Num("smoothStart") ?? 0.0,
            SmoothEnd = Num("smoothEnd") ?? 0.0,
            BounceEnd = Num("bounceEnd") ?? 0.0,
            Amount = Num("amount"),
            Color = e.TryGetProperty("color", out var c) ? ColorFromJson(c) : null,
            Path = Str("path"),
            Paragraph = Int("paragraph"),
            TextBuild = Str("textBuild") switch
            {
                "byParagraph" => TextBuild.ByParagraph,
                "byWord" => TextBuild.ByWord,
                "byLetter" => TextBuild.ByLetter,
                _ => TextBuild.AsOne,
            },
            Trigger = Int("trigger"),
            After = Str("after"),
        };
    }

    /// <summary>
    /// Domyślny schemat kolorów DeckCraft (pierwszy z <c>builtin_color_schemes</c>): dk1, lt1, dk2, lt2, accent1–6, hlink,
    /// folHlink — tak <c>Timeline::new</c> rozwiązuje kolory motywu.
    /// </summary>
    private static readonly string[] DefaultScheme =
        ["000000", "FFFFFF", "1E2B3C", "EDEBE7", "2E6FD8", "F26B1D", "2EA36F", "8A5CD8", "D83F6B", "E3B21C", "2E6FD8", "7A5BA6"];

    /// <summary>Accent1 domyślnego schematu — kolor kształtu bez jednolitego wypełnienia.</summary>
    public static Rgba DefaultAccent { get; } = Rgba.Rgb(0x2E, 0x6F, 0xD8);

    private static Rgba? ColorFromJson(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.String)
            return Rgba.FromHex(c.GetString());
        if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("base", out var b) || b.ValueKind != JsonValueKind.Object)
            return null;
        // przekształcenia (`mods`: lumMod, tint…) nie są przenoszone — kolor bazowy
        var kind = b.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
        return kind switch
        {
            "rgb" when b.TryGetProperty("rgb", out var rgb) => RgbFromJson(rgb),
            "system" when b.TryGetProperty("last", out var last) => RgbFromJson(last),
            "scheme" when b.TryGetProperty("slot", out var slot) && slot.ValueKind == JsonValueKind.String => SchemeSlot(slot.GetString()!),
            _ => null,
        };
    }

    private static Rgba? RgbFromJson(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String)
            return Rgba.FromHex(v.GetString());
        if (v.ValueKind != JsonValueKind.Object)
            return null;
        byte Ch(string name, byte def) => v.TryGetProperty(name, out var x) && x.TryGetByte(out var bt) ? bt : def;
        return new Rgba(Ch("r", 0), Ch("g", 0), Ch("b", 0), Ch("a", 255));
    }

    private static Rgba? SchemeSlot(string slot)
    {
        var index = slot switch
        {
            "dk1" or "tx1" => 0,
            "lt1" or "bg1" => 1,
            "dk2" or "tx2" => 2,
            "lt2" or "bg2" => 3,
            "accent1" or "phClr" => 4,
            "accent2" => 5,
            "accent3" => 6,
            "accent4" => 7,
            "accent5" => 8,
            "accent6" => 9,
            "hlink" => 10,
            "folHlink" => 11,
            _ => -1,
        };
        return index < 0 ? null : Rgba.FromHex(DefaultScheme[index]);
    }
}

/// <summary>Akapit tekstu kształtu dla budowania akapitami: poziom konspektu i czy ma widoczny tekst (<c>build_filled</c>).</summary>
public sealed record ParagraphInfo(int Level, bool Filled);

/// <summary>Rola akapitu w budowaniu akapitami.</summary>
public abstract record BuildRole
{
    private BuildRole()
    {
    }

    /// <summary>Animowany osobno: akapit z tekstem na najpłytszym poziomie z tekstem.</summary>
    public sealed record Top : BuildRole;

    /// <summary>Idzie za akapitem <paramref name="Owner"/> (głębsze, puste akapity po nim).</summary>
    public sealed record Member(int Owner) : BuildRole;

    /// <summary>Przed pierwszym akapitem <see cref="Top"/>: nie jest animowany.</summary>
    public sealed record Unowned : BuildRole;
}

public static class ParagraphBuild
{
    /// <summary><c>MAX_BUILD_PARAGRAPHS</c>: najwięcej akapitów animowanych osobno; dalsze idą za ostatnim z nich.</summary>
    public const int MaxBuildParagraphs = 1_000;

    /// <summary><c>build_filled</c>: czy akapit (jego fragmenty tekstu) pokazuje jakikolwiek tekst.</summary>
    public static bool Filled(IEnumerable<string> runs) => runs.Any(r => !string.IsNullOrWhiteSpace(r));

    /// <summary><c>paragraph_build_roles</c>: rola każdego akapitu (wg indeksu) w budowaniu akapitami.</summary>
    public static IReadOnlyList<BuildRole> Roles(IReadOnlyList<ParagraphInfo> paragraphs)
    {
        var filled = paragraphs.Where(p => p.Filled).ToList();
        var top = filled.Count > 0 ? filled.Min(p => p.Level) : 0;
        var tops = 0;
        int? owner = null;
        var roles = new List<BuildRole>(paragraphs.Count);
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];
            if (p.Level <= top && p.Filled && tops < MaxBuildParagraphs)
            {
                tops++;
                owner = i;
                roles.Add(new BuildRole.Top());
            }
            else
                roles.Add(owner is { } o ? new BuildRole.Member(o) : new BuildRole.Unowned());
        }
        return roles;
    }
}
