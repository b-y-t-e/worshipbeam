// Port of DeckCraft crates/model/src/anim.rs (TRANSITIONS, transition_option_label).
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Przejście z galerii DeckCraft (wiersz <c>TRANSITIONS</c>).</summary>
/// <param name="Id">Identyfikator, np. <c>fade</c>, <c>push</c>, <c>vortex</c>.</param>
/// <param name="Label">Nazwa w PowerPoincie (angielska).</param>
/// <param name="Category">Grupa: <c>Subtle</c>, <c>Exciting</c>, <c>Dynamic Content</c>.</param>
/// <param name="DefaultDurationMs">Domyślny czas trwania w milisekundach.</param>
/// <param name="Options">Opcje efektu (wartości z pliku, np. kierunek <c>l</c>, <c>horz</c>, <c>in</c>).</param>
public sealed record TransitionInfo(string Id, string Label, string Category, int DefaultDurationMs, IReadOnlyList<string> Options);

/// <summary>Galeria przejść DeckCraft: identyfikatory, grupy, opcje i domyślne czasy (<c>TRANSITIONS</c>).</summary>
public static class TransitionCatalog
{
    /// <summary>Wszystkie przejścia, w kolejności galerii.</summary>
    public static IReadOnlyList<TransitionInfo> All { get; } =
    [
        new("none", "None", "Subtle", 0, []),
        new("morph", "Morph", "Subtle", 2000, ["objects", "words", "characters"]),
        new("fade", "Fade", "Subtle", 700, ["smoothly", "throughBlack"]),
        new("push", "Push", "Subtle", 1000, ["u", "d", "l", "r"]),
        new("wipe", "Wipe", "Subtle", 1000, ["r", "l", "u", "d", "ru", "lu", "rd", "ld"]),
        new("split", "Split", "Subtle", 1500, ["vertOut", "vertIn", "horzOut", "horzIn"]),
        new("reveal", "Reveal", "Subtle", 2000, ["smoothlyLeft", "smoothlyRight", "blackLeft", "blackRight"]),
        new("cut", "Cut", "Subtle", 0, ["", "throughBlack"]),
        new("randomBar", "Random Bars", "Subtle", 1000, ["vert", "horz"]),
        new("shape", "Shape", "Subtle", 2000, ["circle", "diamond", "plus", "in", "out"]),
        new("uncover", "Uncover", "Subtle", 1000, ["l", "r", "u", "d", "lu", "ru", "ld", "rd"]),
        new("cover", "Cover", "Subtle", 1000, ["l", "r", "u", "d", "lu", "ru", "ld", "rd"]),
        new("flash", "Flash", "Subtle", 1000, []),
        new("fallOver", "Fall Over", "Exciting", 2000, ["l", "r"]),
        new("drape", "Drape", "Exciting", 2000, ["l", "r"]),
        new("curtains", "Curtains", "Exciting", 2000, []),
        new("wind", "Wind", "Exciting", 2000, ["l", "r"]),
        new("prestige", "Prestige", "Exciting", 2000, []),
        new("fracture", "Fracture", "Exciting", 2000, []),
        new("crush", "Crush", "Exciting", 2000, []),
        new("peelOff", "Peel Off", "Exciting", 1500, ["l", "r"]),
        new("pageCurlDouble", "Page Curl", "Exciting", 1500, ["l", "r", "singleL", "singleR"]),
        new("airplane", "Airplane", "Exciting", 2000, ["l", "r"]),
        new("origami", "Origami", "Exciting", 2000, ["l", "r"]),
        new("dissolve", "Dissolve", "Exciting", 1200, []),
        new("checker", "Checkerboard", "Exciting", 1500, ["horz", "vert"]),
        new("blinds", "Blinds", "Exciting", 1600, ["vert", "horz"]),
        new("clock", "Clock", "Exciting", 2000, ["clockwise", "counterClockwise", "wedge"]),
        new("ripple", "Ripple", "Exciting", 1400, ["center", "lu", "ru", "ld", "rd"]),
        new("honeycomb", "Honeycomb", "Exciting", 3000, []),
        new("glitter", "Glitter", "Exciting", 2500, ["l", "r", "u", "d", "hexL", "hexR", "hexU", "hexD"]),
        new("vortex", "Vortex", "Exciting", 3000, ["l", "r", "u", "d"]),
        new("shred", "Shred", "Exciting", 2000, ["stripsIn", "stripsOut", "particlesIn", "particlesOut"]),
        new("switch", "Switch", "Exciting", 1250, ["l", "r"]),
        new("flip", "Flip", "Exciting", 1250, ["l", "r"]),
        new("gallery", "Gallery", "Exciting", 1500, ["l", "r"]),
        new("cube", "Cube", "Exciting", 1250, ["l", "r", "u", "d"]),
        new("doors", "Doors", "Exciting", 1400, ["vert", "horz"]),
        new("box", "Box", "Exciting", 1250, ["l", "r", "u", "d"]),
        new("comb", "Comb", "Exciting", 1000, ["horz", "vert"]),
        new("zoom", "Zoom", "Exciting", 1000, ["in", "out", "rotate"]),
        new("random", "Random", "Exciting", 2000, []),
        new("pan", "Pan", "Dynamic Content", 1000, ["u", "d", "l", "r"]),
        new("ferris", "Ferris Wheel", "Dynamic Content", 2000, ["l", "r"]),
        new("conveyor", "Conveyor", "Dynamic Content", 2000, ["l", "r"]),
        new("rotate", "Rotate", "Dynamic Content", 2000, ["l", "r", "u", "d"]),
        new("window", "Window", "Dynamic Content", 1250, ["vert", "horz"]),
        new("orbit", "Orbit", "Dynamic Content", 2000, ["l", "r", "u", "d"]),
        new("flythrough", "Fly Through", "Dynamic Content", 1000, ["in", "out", "inBounce", "outBounce"]),
    ];

    private static readonly Dictionary<string, TransitionInfo> ById = All.ToDictionary(t => t.Id, StringComparer.Ordinal);

    /// <summary>Identyfikatory wszystkich przejść (<c>TRANSITIONS</c>).</summary>
    public static IReadOnlyList<string> Kinds { get; } = All.Select(t => t.Id).ToList();

    /// <summary>Czy to przejście z galerii DeckCraft? (Plik może mieć nieznany efekt — wtedy klatki to przenikanie.)</summary>
    public static bool IsKnown(string kind) => ById.ContainsKey(kind);

    /// <summary>Opis przejścia albo <c>null</c>.</summary>
    public static TransitionInfo? Find(string kind) => ById.GetValueOrDefault(kind);

    /// <summary>Opcje efektu przejścia (puste dla nieznanego).</summary>
    public static IReadOnlyList<string> Options(string kind) => Find(kind)?.Options ?? [];

    /// <summary>Domyślny czas trwania w sekundach; nieznane przejście — 1 s (jak czytnik PPTX).</summary>
    public static double DefaultDurationSeconds(string kind) => (Find(kind)?.DefaultDurationMs ?? 1000) / 1000.0;

    /// <summary>
    /// Nazwa opcji efektu, jaką pokazuje PowerPoint (<c>transition_option_label</c>, angielska). Opcje to wartości z
    /// pliku: dla ruchomych przejść kierunek ruchu, więc „l” to „From Right”; Brokat i przejścia gotowe (Przewrócenie,
    /// Wiatr…) nazywają samą stronę; Fala — dokąd zmierza.
    /// </summary>
    public static string OptionLabel(string kind, string option)
    {
        static string Side(char c) => c switch { 'l' => "Left", 'r' => "Right", 'u' => "Top", _ => "Bottom" };
        static char Opposite(char c) => c switch { 'l' => 'r', 'r' => 'l', 'u' => 'd', _ => 'u' };
        static string? Corner(string o, bool flip)
        {
            if (o.Length < 2)
                return null;
            var (h, v) = (o[0], o[1]);
            if (flip)
                (h, v) = (Opposite(h), Opposite(v));
            return $"From {Side(v)}-{Side(h)}";
        }
        var moving = kind is "push" or "wipe" or "cover" or "uncover" or "pan" or "vortex" or "cube" or "box" or "rotate" or "orbit"
            or "gallery" or "conveyor" or "ferris" or "switch" or "flip";
        var len = TransitionGeom.ByteLen(option);
        return (kind, option) switch
        {
            (_, "horz") => "Horizontal",
            (_, "vert") => "Vertical",
            ("split", var o) => $"{(o.StartsWith("horz", StringComparison.Ordinal) ? "Horizontal" : "Vertical")} {(o.EndsWith("In", StringComparison.Ordinal) ? "In" : "Out")}",
            ("fade" or "cut", "throughBlack") => "Through Black",
            ("fade", _) => "Smoothly",
            ("cut", _) => "Cut",
            ("clock", "counterClockwise") => "Counterclockwise",
            ("clock", "wedge") => "Wedge",
            ("clock", _) => "Clockwise",
            ("zoom", "rotate") => "Zoom and Rotate",
            ("ripple", "center") => "Center",
            ("ripple", var o) => Corner(o, true) ?? "Center",
            ("glitter", var o) => GlitterLabel(o),
            ("pageCurlDouble", var o) => o switch
            {
                "singleL" => "Single Left",
                "singleR" => "Single Right",
                "r" => "Double Right",
                _ => "Double Left",
            },
            ("shred", var o) => $"{(o.StartsWith("particles", StringComparison.Ordinal) ? "Particles" : "Strips")} {(o.EndsWith("Out", StringComparison.Ordinal) ? "Out" : "In")}",
            ("reveal", var o) => $"{(o.StartsWith("black", StringComparison.Ordinal) ? "Through Black" : "Smoothly")} {(o.EndsWith("Right", StringComparison.Ordinal) ? "From Right" : "From Left")}",
            ("flythrough", var o) => $"{(o.StartsWith("out", StringComparison.Ordinal) ? "Out" : "In")}{(o.EndsWith("Bounce", StringComparison.Ordinal) ? " with Bounce" : "")}",
            ("morph", var o) => o switch { "words" => "Words", "characters" => "Characters", _ => "Objects" },
            (_, var o) when len == 2 && moving => Corner(o, true) ?? "",
            (_, var o) when len == 1 && moving => $"From {Side(Opposite(o[0]))}",
            (_, "l") => "Left",
            (_, "r") => "Right",
            (_, "u") => "Top",
            (_, "d") => "Bottom",
            (_, "in") => "In",
            (_, "out") => "Out",
            (_, var o) => o,
        };

        static string GlitterLabel(string o)
        {
            var (pat, d) = o.StartsWith("hex", StringComparison.Ordinal) ? ("Hexagons", o[3..].ToLowerInvariant()) : ("Diamonds", o);
            return $"{pat} From {Side(d.Length > 0 ? d[0] : 'l')}";
        }
    }
}
