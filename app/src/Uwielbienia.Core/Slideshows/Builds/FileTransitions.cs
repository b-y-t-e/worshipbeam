// Port of the transition reading of DeckCraft's PPTX import (craft-pptx: read/timing.rs `transition`, maps.rs
// `transition_from_xml`; craft-drawingml xmlx.rs `resolve_mc` for mc:AlternateContent).
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Przejście slajdu zapisane w pliku prezentacji.</summary>
/// <param name="Kind">Identyfikator przejścia DeckCraft (<see cref="TransitionCatalog.Kinds"/>, np. <c>fade</c>, <c>push</c>,
/// <c>vortex</c>); efekt, którego DeckCraft nie zna — nazwa elementu z pliku (klatki: przenikanie); <c>none</c> — slajd bez
/// efektu, ale z czasem przejścia, dźwiękiem albo bez przejścia na kliknięcie.</param>
/// <param name="Option">Opcja efektu (np. kierunek <c>l</c>), pusta dla przejść bez opcji.</param>
/// <param name="DurationSeconds">Czas trwania w sekundach.</param>
public sealed record FileTransition(string Kind, string Option, double DurationSeconds);

/// <summary>
/// Odczyt przejść slajdów z pliku PowerPoint (<c>.pptx .ppsx .pptm .ppsm</c>) tak, jak czyta je DeckCraft: element
/// <c>p:transition</c> (także w <c>mc:AlternateContent</c> — PowerPoint 2010+ zapisuje efekty <c>p14</c>/<c>p15</c>
/// z zastępczym <c>mc:Fallback</c>), efekt i opcja według mapy DeckCraft, czas z <c>p14:dur</c> albo <c>spd</c>.
/// </summary>
public static class FileTransitions
{
    /// <summary>Przedrostki <c>mc:Choice Requires</c>, które czytnik DeckCraft rozumie (<c>SUPPORTED_MC</c>).</summary>
    private static readonly string[] SupportedMc = ["p14", "p15", "p159", "a14", "a16", "p1510", "a15", "c14", "p188", "cx1"];

    /// <summary>Przejścia, które przekładają się jeden do jednego: (nasz rodzaj, element).</summary>
    private static readonly (string Kind, string Element)[] Simple =
        [("dissolve", "dissolve"), ("random", "random"), ("honeycomb", "honeycomb"), ("flash", "flash")];

    /// <summary>Gotowe przejścia (<c>p15:prstTrans</c>), których opcję „Left” PowerPoint zapisuje jako <c>invX="1"</c> (pozostałe — „Right”).</summary>
    private static readonly string[] InvXLeft = ["wind", "airplane", "origami"];

    private const string OfficeDocumentRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string StrictOfficeDocumentRel = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";

    /// <summary>
    /// Przejście każdego slajdu w kolejności <c>p:sldIdLst</c>, także slajdów ukrytych (<c>null</c> = bez przejścia).
    /// Pusta lista, gdy pliku nie da się przeczytać jako prezentacji OOXML.
    /// </summary>
    public static IReadOnlyList<FileTransition?> Read(string presentation)
    {
        try
        {
            using var zip = ZipFile.OpenRead(presentation);
            var deckPart = MainPart(zip);
            if (deckPart is null || Load(zip, deckPart) is not { } deck)
                return [];
            var rels = Relationships(zip, deckPart);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<FileTransition?>();
            foreach (var id in Child(deck, "sldIdLst")?.Elements().Where(e => e.Name.LocalName == "sldId") ?? [])
            {
                if (!rels.TryGetValue(Attr(id, "id", prefixedOnly: true) ?? "", out var part) || !seen.Add(part))
                    continue;
                if (Load(zip, part) is not { } slide)
                    continue;
                result.Add(ResolvedChildren(slide).FirstOrDefault(e => e.Name.LocalName == "transition") is { } t ? Transition(t) : null);
            }
            return result;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>Przejście z elementu <c>p:transition</c> (<c>read/timing.rs: transition</c>).</summary>
    private static FileTransition? Transition(XElement t)
    {
        var effect = ResolvedChildren(t).FirstOrDefault(e => e.Name.LocalName is not ("sndAc" or "extLst"));
        var advanceOnClick = ToBool(Attr(t, "advClick")) ?? true;
        var advanceAfter = MsAttr(t, "advTm");
        string kind, option = "";
        if (effect is not null && FromXml(effect) is { } mapped)
            (kind, option) = mapped;
        else
            kind = effect?.Name.LocalName ?? "none";
        var defaultMs = TransitionCatalog.Find(kind)?.DefaultDurationMs ?? 1000;
        // `spd` ma w schemacie domyślnie „fast”; PowerPoint dla 0,5 s pomija oba atrybuty
        var durationMs = MsAttr(t, "dur") ?? Attr(t, "spd") switch
        {
            "med" => 750,
            "slow" => Math.Max(defaultMs, 1000),
            _ => 500,
        };
        var sound = ResolvedPath(t, "sndAc", "stSnd", "snd") is { } snd && !string.IsNullOrEmpty(Attr(snd, "embed", prefixedOnly: true));
        var stopSound = ResolvedPath(t, "sndAc", "endSnd") is not null;
        if (kind == "none" && advanceOnClick && advanceAfter is null && !sound && !stopSound)
            return null;
        return new FileTransition(kind, option, durationMs / 1000.0);
    }

    /// <summary>Efekt przejścia → (rodzaj, opcja); <c>null</c> dla efektów, których DeckCraft nie modeluje (<c>maps.rs: transition_from_xml</c>).</summary>
    private static (string Kind, string Option)? FromXml(XElement e)
    {
        if (FromXmlRaw(e) is not var (k, o))
            return null;
        // rodzaje bez opcji nie mają żadnej
        var hasOptions = TransitionCatalog.Find(k) is not { } info || info.Options.Count > 0;
        return (k, hasOptions ? o : "");
    }

    private static (string Kind, string Option)? FromXmlRaw(XElement e)
    {
        // domyślne są tym, co PowerPoint pomija: np. Wypychanie w lewo zapisuje jako <p:push/>
        string Dir(string d) => Attr(e, "dir") ?? d;
        bool Flag(string name) => ToBool(Attr(e, name)) == true;
        switch (e.Name.LocalName)
        {
            case "fade": return ("fade", Flag("thruBlk") ? "throughBlack" : "smoothly");
            case "cut": return ("cut", Flag("thruBlk") ? "throughBlack" : "");
            case "push": return ("push", Dir("l"));
            case "wipe": return ("wipe", Dir("l"));
            // wycieranie po przekątnej
            case "strips": return ("wipe", Dir("lu"));
            case "split":
            {
                var orient = Attr(e, "orient") ?? "horz";
                var d = Attr(e, "dir") ?? "out";
                return ("split", orient + (d == "in" ? "In" : "Out"));
            }
            case "randomBar": return ("randomBar", Dir("horz"));
            case "circle": return ("shape", "circle");
            case "diamond": return ("shape", "diamond");
            case "plus": return ("shape", "plus");
            // Kształt do środka / na zewnątrz (slajd powiększany w ramce)
            case "zoom": return ("shape", Attr(e, "dir") == "in" ? "in" : "out");
            // przejście Powiększenie i jego opcja Powiększenie z obrotem
            case "warp": return ("zoom", Attr(e, "dir") == "in" ? "in" : "out");
            case "newsflash": return ("zoom", "rotate");
            case "pull": return ("uncover", Dir("l"));
            case "cover": return ("cover", Dir("l"));
            case "checker": return ("checker", Dir("horz"));
            case "blinds": return ("blinds", Dir("horz"));
            case "comb": return ("comb", Dir("horz"));
            case "wheel": return ("clock", "clockwise");
            case "wheelReverse": return ("clock", "counterClockwise");
            case "wedge": return ("clock", "wedge");
            case "vortex": return ("vortex", Dir("l"));
            case "switch": return ("switch", Dir("r"));
            case "flip": return ("flip", Dir("r"));
            case "ripple": return ("ripple", Dir("center"));
            case "doors": return ("doors", Dir("horz"));
            case "window": return ("window", Dir("horz"));
            case "ferris": return ("ferris", Dir("l"));
            case "gallery": return ("gallery", Dir("l"));
            case "conveyor": return ("conveyor", Dir("l"));
            case "pan": return ("pan", Dir("l"));
            case "glitter":
            {
                var d = Dir("l");
                return Attr(e, "pattern") == "hexagon" ? ("glitter", "hex" + AsciiUpper(d)) : ("glitter", d);
            }
            case "shred":
            {
                // `crush` zapisywał DeckCraft, zanim poznał `rectangle` PowerPointa
                var particles = Attr(e, "pattern") is "rectangle" or "crush";
                var @out = Attr(e, "dir") == "out";
                return ("shred", (particles ? "particles" : "strips") + (@out ? "Out" : "In"));
            }
            case "reveal":
                return ("reveal", (Flag("thruBlk") ? "black" : "smoothly") + (Attr(e, "dir") == "r" ? "Right" : "Left"));
            case "flythrough":
                return ("flythrough", (Attr(e, "dir") == "out" ? "out" : "in") + (Flag("hasBounce") ? "Bounce" : ""));
            case "prism":
            {
                var kind = (Flag("isContent"), Flag("isInverted")) switch
                {
                    (false, false) => "cube",
                    (false, true) => "box",
                    (true, false) => "rotate",
                    (true, true) => "orbit",
                };
                return (kind, Dir("l"));
            }
            case "prstTrans":
            {
                var prst = Attr(e, "prst") ?? "fallOver";
                var inv = Flag("invX");
                if (prst == "pageCurlSingle")
                    return ("pageCurlDouble", inv ? "singleR" : "singleL");
                if (!TransitionCatalog.IsKnown(prst))
                    return null;
                var left = InvXLeft.Contains(prst) ? inv : !inv;
                return (prst, left ? "l" : "r");
            }
            case "morph":
                return ("morph", Attr(e, "option") switch { "byWord" => "words", "byChar" => "characters", _ => "objects" });
            default:
                return Simple.FirstOrDefault(s => s.Element == e.Name.LocalName) is { Kind: not null } s ? (s.Kind, "") : null;
        }
    }

    // ---------- XML ----------

    /// <summary>
    /// Elementy dzieci po rozwiązaniu <c>mc:AlternateContent</c> (<c>xmlx.rs: resolve_mc</c>): pierwszy
    /// <c>mc:Choice</c>, którego wszystkie przedrostki <c>Requires</c> czytnik rozumie (i bez <c>contentPart</c>),
    /// inaczej <c>mc:Fallback</c>; zagnieżdżone <c>AlternateContent</c> tak samo.
    /// </summary>
    private static IEnumerable<XElement> ResolvedChildren(XElement el, int depth = 0)
    {
        foreach (var c in el.Elements())
        {
            if (c.Name.LocalName != "AlternateContent")
            {
                yield return c;
                continue;
            }
            if (depth > 64 || Branch(c) is not { } branch)
                continue;
            foreach (var inner in ResolvedChildren(branch, depth + 1))
                yield return inner;
        }
    }

    private static XElement? Branch(XElement alternate) =>
        alternate.Elements().FirstOrDefault(ch =>
        {
            if (ch.Name.LocalName != "Choice")
                return false;
            var requires = Attr(ch, "Requires") ?? "";
            var understood = requires.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(SupportedMc.Contains);
            var modelled = !ch.Elements().Any(e => e.Name.LocalName == "contentPart")
                && (!requires.Contains("cx1", StringComparison.Ordinal)
                    || ch.Descendants().Any(g => g.Name.LocalName == "graphicData" && (Attr(g, "uri")?.EndsWith("/chartex", StringComparison.Ordinal) ?? false)));
            return understood && modelled;
        }) ?? alternate.Elements().FirstOrDefault(e => e.Name.LocalName == "Fallback");

    /// <summary>
    /// Atrybut po nazwie lokalnej (<c>attr_local</c>): najpierw bez przedrostka, potem z dowolnym przedrostkiem
    /// (np. <c>dur</c> → <c>p14:dur</c>); <paramref name="prefixedOnly"/> — tylko z przedrostkiem (<c>r:id</c>).
    /// </summary>
    private static string? Attr(XElement e, string name, bool prefixedOnly = false)
    {
        var attrs = e.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName == name);
        return (prefixedOnly ? null : attrs.FirstOrDefault(a => a.Name.Namespace == XNamespace.None)?.Value)
            ?? attrs.FirstOrDefault(a => a.Name.Namespace != XNamespace.None)?.Value;
    }

    private static XElement? Child(XElement e, string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name);

    private static XElement? ResolvedPath(XElement element, params string[] names) =>
        names.Aggregate<string, XElement?>(element, (parent, name) =>
            parent is null ? null : ResolvedChildren(parent).FirstOrDefault(child => child.Name.LocalName == name));

    /// <summary>Wartość logiczna jak <c>craft_xml::value::to_bool</c>.</summary>
    private static bool? ToBool(string? s) => s?.Trim() switch
    {
        "1" or "true" or "on" or "t" => true,
        "0" or "false" or "off" or "f" => false,
        _ => null,
    };

    /// <summary>Milisekundy z atrybutu (<c>ms_attr</c>): <c>indefinite</c> i nie-liczby → <c>null</c>, w granicach doby.</summary>
    private static int? MsAttr(XElement e, string name)
    {
        var v = Attr(e, name)?.Trim();
        if (v is null || v == "indefinite")
            return null;
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || !double.IsFinite(ms))
            return null;
        return (int)Math.Clamp(ms, 0.0, 86_400_000.0);
    }

    private static string AsciiUpper(string s) => string.Concat(s.Select(ch => ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch));

    // ---------- pakiet ----------

    private static XElement? Load(ZipArchive zip, string part)
    {
        var entry = zip.GetEntry(part) ?? zip.Entries.FirstOrDefault(x => string.Equals(x.FullName, part, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;
        using var stream = entry.Open();
        return XDocument.Load(stream).Root;
    }

    /// <summary>Część głównego dokumentu (zwykle <c>ppt/presentation.xml</c>) z <c>_rels/.rels</c>.</summary>
    private static string? MainPart(ZipArchive zip)
    {
        var root = Load(zip, "_rels/.rels");
        var target = root?.Elements().FirstOrDefault(r => Attr(r, "Type") is OfficeDocumentRel or StrictOfficeDocumentRel) is { } rel
            ? Attr(rel, "Target")
            : null;
        return target is null ? "ppt/presentation.xml" : ResolveTarget("", target);
    }

    /// <summary>Powiązania części: identyfikator → pełna nazwa części docelowej (bez powiązań zewnętrznych).</summary>
    private static Dictionary<string, string> Relationships(ZipArchive zip, string part)
    {
        var slash = part.LastIndexOf('/');
        var dir = slash < 0 ? "" : part[..(slash + 1)];
        var name = slash < 0 ? part : part[(slash + 1)..];
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in Load(zip, dir + "_rels/" + name + ".rels")?.Elements() ?? [])
            if (Attr(r, "Id") is { } id && Attr(r, "Target") is { } target && Attr(r, "TargetMode") != "External")
                map.TryAdd(id, ResolveTarget(dir, target));
        return map;
    }

    /// <summary>Cel powiązania względem folderu części źródłowej (z <c>..</c> i ścieżkami od korzenia pakietu).</summary>
    private static string ResolveTarget(string dir, string target)
    {
        target = Uri.UnescapeDataString(target.Replace('\\', '/'));
        var parts = new List<string>();
        foreach (var segment in ((target.StartsWith('/') ? "" : dir) + target).Split('/'))
        {
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
            }
            else
                parts.Add(segment);
        }
        return string.Join('/', parts);
    }
}
