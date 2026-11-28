using System.IO.Compression;
using System.Xml.Linq;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.Core.Slideshows;

/// <summary>
/// To, czego DeckCraft nie podaje, a czego potrzebuje budowa slajdu (<see cref="SlideBuild"/>): kształty ukryte w pliku
/// i akapity tekstu (poziom, czy mają tekst). Oraz kopie prezentacji do rysowania warstw akapitów — DeckCraft ukrywa
/// tylko całe kształty, więc akapity ukrywamy w kopii pliku (tekst przezroczysty, jak przy krokach animacji).
/// </summary>
public static class BuildSources
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Kształt leżący wprost na slajdzie.</summary>
    /// <param name="Hidden">Ukryty w okienku zaznaczania — nie rysuje się i nie wolno go pokazać.</param>
    public sealed record SourceShape(int Id, bool Hidden, IReadOnlyList<ParagraphInfo> Paragraphs);

    /// <summary>Zmiana kształtu w kopii: akapity do ukrycia; <paramref name="TextOnly"/> = bez wypełnienia, obrysu i efektów (sam tekst).</summary>
    public sealed record ShapeEdit(IReadOnlySet<int> HiddenParagraphs, bool TextOnly);

    /// <summary>Kształty każdego slajdu (wszystkich, także ukrytych — numeracja jak w DeckCraft), według identyfikatora.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<int, SourceShape>> Read(string presentation)
    {
        using var zip = ZipFile.OpenRead(presentation);
        var slides = new List<IReadOnlyDictionary<int, SourceShape>>();
        foreach (var part in SlideParts(zip))
        {
            var shapes = new Dictionary<int, SourceShape>();
            if (Load(zip, part) is { } slide)
                foreach (var shape in TopShapes(slide.Root!))
                    if (Id(shape) is { } id && !shapes.ContainsKey(id))
                        shapes[id] = new SourceShape(id, IsHidden(shape), Paragraphs(shape));
            slides.Add(shapes);
        }
        return slides;
    }

    /// <summary>Kopia prezentacji ze zmianami kształtów (indeks slajdu jak w <see cref="Read"/> → kształt → zmiana).</summary>
    public static void WriteCopy(string source, string target, IReadOnlyDictionary<int, IReadOnlyDictionary<int, ShapeEdit>> edits)
    {
        File.Copy(source, target, overwrite: true);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Update);
        var parts = SlideParts(zip);
        foreach (var (index, shapes) in edits)
        {
            if (index < 0 || index >= parts.Count || Load(zip, parts[index]) is not { } slide)
                continue;
            foreach (var shape in TopShapes(slide.Root!))
            {
                if (Id(shape) is not { } id || !shapes.TryGetValue(id, out var edit))
                    continue;
                foreach (var paragraph in edit.HiddenParagraphs)
                    AnimationSteps.HideParagraphs(shape, paragraph, paragraph, preserveBulletNumbering: true);
                if (edit.TextOnly)
                    TextOnly(shape);
            }
            zip.GetEntry(parts[index])?.Delete();
            using var stream = zip.CreateEntry(parts[index]).Open();
            slide.Save(stream, SaveOptions.DisableFormatting);
        }
    }

    private static List<string> SlideParts(ZipArchive zip)
    {
        if (Load(zip, "ppt/presentation.xml")?.Root is not { } deck)
            return [];
        var targets = Load(zip, "ppt/_rels/presentation.xml.rels")?.Root?.Elements(Rel + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .GroupBy(e => (string)e.Attribute("Id")!)
            .ToDictionary(g => g.Key, g => (string)g.First().Attribute("Target")!) ?? [];
        return deck.Element(P + "sldIdLst")?.Elements(P + "sldId")
            .Select(e => (string?)e.Attribute(R + "id"))
            .Where(id => id is not null && targets.ContainsKey(id))
            .Select(id => "ppt/" + targets[id!].TrimStart('/').Replace("ppt/", "", StringComparison.Ordinal).Replace("../", "", StringComparison.Ordinal))
            .ToList() ?? [];
    }

    private static IEnumerable<XElement> TopShapes(XElement slide) =>
        slide.Element(P + "cSld")?.Element(P + "spTree")?.Elements()
            .Where(e => e.Name == P + "sp" || e.Name == P + "pic" || e.Name == P + "grpSp" || e.Name == P + "graphicFrame" || e.Name == P + "cxnSp")
        ?? [];

    private static XElement? Properties(XElement shape) =>
        shape.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("nv", StringComparison.Ordinal))?.Element(P + "cNvPr");

    private static int? Id(XElement shape) => (int?)Properties(shape)?.Attribute("id");

    private static bool IsHidden(XElement shape) => (string?)Properties(shape)?.Attribute("hidden") is "1" or "true";

    /// <summary>Akapity tekstu kształtu: poziom i czy jest w nich tekst (jak <c>build_filled</c> w DeckCraft).</summary>
    private static IReadOnlyList<ParagraphInfo> Paragraphs(XElement shape) =>
        shape.Element(P + "txBody")?.Elements(A + "p")
            .Select(p => new ParagraphInfo(
                (int?)p.Element(A + "pPr")?.Attribute("lvl") ?? 0,
                p.Elements().Where(e => e.Name == A + "r" || e.Name == A + "fld").Any(r => !string.IsNullOrWhiteSpace((string?)r.Element(A + "t")))))
            .ToList()
        ?? [];

    private static readonly string[] Drawn = ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "ln", "effectLst", "effectDag", "scene3d", "sp3d"];

    /// <summary>Sam tekst kształtu: jawnie bez wypełnienia, obrysu i efektów (inaczej doszłyby ze stylu kształtu).</summary>
    private static void TextOnly(XElement shape)
    {
        var properties = shape.Element(P + "spPr");
        if (properties is null)
        {
            properties = new XElement(P + "spPr");
            Properties(shape)?.Parent?.AddAfterSelf(properties);
        }
        properties.Elements().Where(e => Drawn.Contains(e.Name.LocalName)).Remove();
        XElement[] none = [new XElement(A + "noFill"), new XElement(A + "ln", new XElement(A + "noFill")), new XElement(A + "effectLst")];
        // kolejność wg schematu: xfrm, geometria, wypełnienie, obrys, efekty, …
        var geometry = properties.Elements().LastOrDefault(e => e.Name.LocalName is "xfrm" or "prstGeom" or "custGeom");
        if (geometry is not null)
            geometry.AddAfterSelf(none);
        else
            properties.AddFirst(none);
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
            return null;
        using var stream = entry.Open();
        try
        {
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
