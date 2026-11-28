using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.Core.Slideshows;

/// <summary>
/// Przejścia slajdów zapisane w prezentacji (PowerPoint: „Przejścia”) — część jej wyglądu, jak animacje. Zapis obok
/// slajdów (<see cref="MapFile"/>): nazwa pliku slajdu → przejście, które do niego prowadzi. Przejście ma tylko pierwszy
/// obraz slajdu; kolejne kroki animacji tego samego slajdu go nie mają.
/// </summary>
public static class SlideFileTransitions
{
    public const string MapFile = "przejscia.json";

    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Zapisuje przejścia slajdów. <paramref name="imagesPerSlide"/> = ile obrazów ma każdy widoczny slajd (kroki animacji,
    /// ruch elementów); <c>null</c> = jeden. Gdy liczba obrazów się nie zgadza, nie zapisuje nic — slajdy działają jak dotąd.
    /// </summary>
    public static void Export(string presentation, string slidesDir, IReadOnlyList<int>? imagesPerSlide = null)
    {
        try
        {
            if (!SlideAnimations.IsOoxml(presentation))
                return;
            var transitions = FileTransitions.Read(presentation);
            var hidden = HiddenSlides(presentation);
            if (hidden.Count != transitions.Count)
                return;
            var visible = transitions.Where((_, i) => !hidden[i]).ToList();
            var counts = imagesPerSlide ?? visible.Select(_ => 1).ToList();
            var images = Directory.EnumerateFiles(slidesDir, "*.png").Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (counts.Count != visible.Count || counts.Sum() != images.Count || !visible.Any(IsVisual))
                return;
            var map = new Dictionary<string, FileTransition>();
            var first = 0;
            for (var i = 0; i < visible.Count; i++)
            {
                if (visible[i] is { } transition && IsVisual(transition))
                    map[images[first]!] = transition;
                first += counts[i];
            }
            File.WriteAllText(Path.Combine(slidesDir, MapFile), JsonSerializer.Serialize(map));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            // przejścia to dodatek — bez nich slajdy przechodzą tak, jak ustawiono w planie
        }
    }

    /// <summary>
    /// Przejście, które coś pokazuje. Slajd z samym czasem albo dźwiękiem przejścia (rodzaj <c>none</c>) nie ma efektu,
    /// więc przechodzi tak, jak ustawiono w planie.
    /// </summary>
    private static bool IsVisual(FileTransition? transition) => transition is { Kind: not "none" };

    /// <summary>Przejścia slajdów (nazwa pliku slajdu → przejście); pusty, gdy ich nie ma.</summary>
    public static IReadOnlyDictionary<string, FileTransition> Load(string slidesDir)
    {
        var file = Path.Combine(slidesDir, MapFile);
        if (!File.Exists(file))
            return new Dictionary<string, FileTransition>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FileTransition>>(File.ReadAllText(file))
                   ?? new Dictionary<string, FileTransition>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, FileTransition>();
        }
    }

    /// <summary>Które slajdy (kolejność z <c>p:sldIdLst</c>) są ukryte — eksport je pomija.</summary>
    private static List<bool> HiddenSlides(string presentation)
    {
        using var zip = ZipFile.OpenRead(presentation);
        XDocument? Load(string name)
        {
            if (zip.GetEntry(name) is not { } entry)
                return null;
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        if (Load("ppt/presentation.xml")?.Root is not { } deck)
            return [];
        var targets = Load("ppt/_rels/presentation.xml.rels")?.Root?.Elements(Rel + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .GroupBy(e => (string)e.Attribute("Id")!)
            .ToDictionary(g => g.Key, g => (string)g.First().Attribute("Target")!) ?? [];
        return deck.Element(P + "sldIdLst")?.Elements(P + "sldId")
            .Select(e => (string?)e.Attribute(R + "id"))
            .Where(id => id is not null && targets.ContainsKey(id))
            .Select(id => "ppt/" + targets[id!].TrimStart('/').Replace("ppt/", "", StringComparison.Ordinal).Replace("../", "", StringComparison.Ordinal))
            .Select(part => (string?)Load(part)?.Root?.Attribute("show") is "0" or "false")
            .ToList() ?? [];
    }
}
