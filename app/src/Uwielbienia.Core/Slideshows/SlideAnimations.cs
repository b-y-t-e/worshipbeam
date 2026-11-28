using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Uwielbienia.Core.Slideshows;

/// <summary>Ruchomy obrazek (animowany GIF) na slajdzie prezentacji.</summary>
/// <param name="File">Pełna ścieżka wyciągniętego pliku GIF (składa ją odczyt opisu — zapisana jest sama nazwa).</param>
/// <param name="X">Lewy brzeg na slajdzie, 0..1 szerokości slajdu.</param>
/// <param name="Y">Górny brzeg, 0..1 wysokości slajdu.</param>
/// <param name="Width">Szerokość, 0..1 szerokości slajdu.</param>
/// <param name="Height">Wysokość, 0..1 wysokości slajdu.</param>
public sealed record SlideAnimation(string File, double X, double Y, double Width, double Height);

/// <summary>
/// Animowane GIF-y z prezentacji: eksport zamienia każdy slajd w nieruchomy PNG, więc GIF zastyga na pierwszej klatce.
/// Tutaj wyciągamy takie obrazki obok slajdów i zapamiętujemy, gdzie na slajdzie stoją — rzutnik rysuje je na PNG
/// (<c>Views/AnimatedSlideImage</c>), a slajd pozostaje zwykłym slajdem prezentacji.
/// Tylko OOXML (<c>.pptx .ppsx .pptm .ppsm</c>) i tylko obrazki wstawione wprost na slajd (nie w grupie), bez obrotu,
/// odbicia, przycięcia i nietypowego wypełnienia — reszta zostaje nieruchoma, tak jak dotąd.
/// W opisie zapisujemy sama nazwę pliku GIF: folder slajdów powstaje pod nazwą tymczasową, a potem jest przenoszony
/// (i kopiowany razem z prezentacją przy „Duplikuj”), więc bezwzględna ścieżka przestałaby istnieć.
/// </summary>
public static class SlideAnimations
{
    /// <summary>Opis ruchomych obrazków zapisany obok slajdów (nazwa pliku PNG → obrazki).</summary>
    public const string MapFile = "animacje.json";

    /// <summary>Zapis opisu: nazwa pliku GIF obok slajdów (bez folderu) i miejsce na slajdzie.</summary>
    private sealed record StoredAnimation(string Name, double X, double Y, double Width, double Height);

    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string SlideRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";

    /// <summary>Domyślny rozmiar slajdu w EMU, gdy prezentacja go nie podaje (4:3).</summary>
    private const double DefaultWidth = 9144000, DefaultHeight = 6858000;

    /// <summary>
    /// Zapisuje w folderze slajdów ruchome obrazki i <see cref="MapFile"/>. Opis powstaje tylko wtedy, gdy liczba
    /// slajdów prezentacji zgadza się z liczbą wyeksportowanych obrazów — inaczej nie wiadomo, który GIF należy do
    /// którego slajdu i lepiej zostawić slajdy nieruchome. Pusty opis też się zapisuje: wtedy wiadomo, że już szukano.
    /// </summary>
    /// <param name="imagesPerSlide">
    /// Ile obrazów ma każdy widoczny slajd (ruch elementów — <see cref="SlideBuild"/>: stan po każdym kliknięciu to osobny
    /// obraz z tymi samymi ruchomymi obrazkami); <c>null</c> = jeden obraz na slajd.
    /// </param>
    public static void Export(string presentation, string slidesDir, IReadOnlyList<int>? imagesPerSlide = null)
    {
        var map = new Dictionary<string, List<StoredAnimation>>();
        try
        {
            var slides = ExportedSlides(slidesDir);
            // najpierw same miejsca obrazków — pliki GIF wypakowujemy dopiero, gdy wiadomo, że opis powstanie
            if (Read(presentation) is { } found && PerImage(found, imagesPerSlide) is { } pictures && pictures.Count == slides.Count)
                Save(presentation, pictures, slides, slidesDir, map);
            File.WriteAllText(Path.Combine(slidesDir, MapFile), JsonSerializer.Serialize(map));
        }
        catch (Exception)
        {
            // Ruchome obrazki to dodatek do slajdów: cokolwiek się tu nie uda (nietypowy albo uszkodzony plik, zajęty
            // folder), slajdy mają działać dalej — zostają nieruchome, jak dotąd.
        }
    }

    /// <summary>Obrazki slajdów powtórzone dla każdego obrazu slajdu; <c>null</c>, gdy liczba slajdów się nie zgadza.</summary>
    private static List<List<FoundAnimation>>? PerImage(List<List<FoundAnimation>> slides, IReadOnlyList<int>? imagesPerSlide)
    {
        if (imagesPerSlide is null)
            return slides;
        if (imagesPerSlide.Count != slides.Count)
            return null;
        return slides.SelectMany((pictures, i) => Enumerable.Repeat(pictures, Math.Max(0, imagesPerSlide[i]))).ToList();
    }

    /// <summary>Ruchome obrazki slajdów (nazwa pliku PNG → obrazki); pusty słownik, gdy ich nie ma.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SlideAnimation>> Load(string slidesDir)
    {
        var file = Path.Combine(slidesDir, MapFile);
        if (!File.Exists(file))
            return new Dictionary<string, IReadOnlyList<SlideAnimation>>();
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, List<StoredAnimation>>>(File.ReadAllText(file));
            return map is null
                ? new Dictionary<string, IReadOnlyList<SlideAnimation>>()
                : map.ToDictionary(p => p.Key, p => Found(p.Value, slidesDir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, IReadOnlyList<SlideAnimation>>();
        }
    }

    /// <summary>Obrazki z opisu — z pełną ścieżką obok slajdów; pominięte te, których pliku już nie ma.</summary>
    private static IReadOnlyList<SlideAnimation> Found(IEnumerable<StoredAnimation> stored, string slidesDir) =>
        stored
            .Select(a => new SlideAnimation(Path.Combine(slidesDir, a.Name), a.X, a.Y, a.Width, a.Height))
            .Where(a => File.Exists(a.File))
            .ToList();

    /// <summary>Opis ruchomych obrazków już powstał (nie trzeba przeglądać prezentacji).</summary>
    public static bool IsDescribed(string slidesDir) => File.Exists(Path.Combine(slidesDir, MapFile));

    public static bool IsOoxml(string file) =>
        AnimationSteps.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant());

    /// <summary>Ruchomy obrazek znaleziony w prezentacji: część pakietu z GIF-em i miejsce na slajdzie.</summary>
    private sealed record FoundAnimation(string Part, StoredAnimation Rect);

    /// <summary>
    /// Ruchome obrazki każdego widocznego slajdu, w kolejności pokazu; <c>null</c> = nie da się przeczytać.
    /// Samo czytanie prezentacji — nic nie zapisuje na dysk.
    /// </summary>
    private static List<List<FoundAnimation>>? Read(string presentation)
    {
        if (!IsOoxml(presentation) || !File.Exists(presentation))
            return null;
        using var zip = ZipFile.OpenRead(presentation);
        if (Part(zip, "ppt/presentation.xml") is not { } deck)
            return null;
        var (slideWidth, slideHeight) = SlideSize(deck);
        var found = new List<List<FoundAnimation>>();
        foreach (var name in SlideParts(zip, deck))
        {
            if (Part(zip, name) is not { } slide)
                return null;
            if (IsHidden(slide))
                continue;
            found.Add(AnimatedPictures(zip, slide, name, slideWidth, slideHeight)
                .Select(picture => new FoundAnimation(picture.Target, picture.Rect))
                .ToList());
        }
        return found;
    }

    /// <summary>
    /// Wypakowuje znalezione GIF-y obok slajdów (<c>ruch001.gif</c>…) i wpisuje je do opisu. Ten sam obrazek stojący
    /// na kilku slajdach zapisuje się raz — inaczej ozdobny GIF powtarzałby się na dysku i w pamięci tyle razy, na ilu
    /// slajdach stoi.
    /// </summary>
    private static void Save(
        string presentation,
        List<List<FoundAnimation>> pictures,
        List<string> slides,
        string slidesDir,
        Dictionary<string, List<StoredAnimation>> map)
    {
        using var zip = ZipFile.OpenRead(presentation);
        var saved = new Dictionary<string, string>();
        for (var i = 0; i < slides.Count; i++)
        {
            if (pictures[i].Count == 0)
                continue;
            map[Path.GetFileName(slides[i])] = pictures[i]
                .Select(picture => picture.Rect with { Name = SavedOnce(zip, picture.Part, slidesDir, saved) })
                .ToList();
        }
    }

    /// <summary>Nazwa pliku GIF obok slajdów; przy powtórzonym obrazku ta sama co poprzednio.</summary>
    private static string SavedOnce(ZipArchive zip, string part, string slidesDir, Dictionary<string, string> saved)
    {
        if (saved.TryGetValue(part, out var known))
            return known;
        var name = $"ruch{saved.Count + 1:D3}.gif";
        Extract(zip, part, Path.Combine(slidesDir, name));
        saved[part] = name;
        return name;
    }

    /// <summary>Obrazy slajdów wyeksportowane do folderu, w kolejności nazw.</summary>
    private static List<string> ExportedSlides(string slidesDir) =>
        Directory.Exists(slidesDir)
            ? Directory.EnumerateFiles(slidesDir, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    private static (double Width, double Height) SlideSize(XElement deck)
    {
        var size = deck.Element(P + "sldSz");
        var width = (double?)size?.Attribute("cx") ?? DefaultWidth;
        var height = (double?)size?.Attribute("cy") ?? DefaultHeight;
        return (width > 0 ? width : DefaultWidth, height > 0 ? height : DefaultHeight);
    }

    /// <summary>Części slajdów w kolejności pokazu (z <c>presentation.xml</c> i jego powiązań).</summary>
    private static List<string> SlideParts(ZipArchive zip, XElement deck)
    {
        var links = Part(zip, "ppt/_rels/presentation.xml.rels");
        var targets = links?.Elements(Rel + "Relationship")
            .Where(e => (string?)e.Attribute("Type") == SlideRelType)
            .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!)
            ?? [];
        return deck.Element(P + "sldIdLst")?.Elements(P + "sldId")
            .Select(e => (string?)e.Attribute(R + "id"))
            .Where(id => id is not null && targets.ContainsKey(id))
            .Select(id => "ppt/" + targets[id!].TrimStart('/').Replace("../", ""))
            .ToList() ?? [];
    }

    /// <summary>Slajd ukryty — eksport go pomija.</summary>
    private static bool IsHidden(XElement slide) => (string?)slide.Attribute("show") == "0";

    /// <summary>
    /// Obrazy wstawione wprost na slajd (<c>p:pic</c> w drzewie kształtów slajdu), które są animowanymi GIF-ami: nazwa
    /// części z obrazem i jego miejsce na slajdzie. Obrazki w grupie, obrócone, odbite, przycięte, wypełniające ramkę
    /// inaczej niż całym obrazem oraz **zakryte czymkolwiek, co leży na slajdzie nad nimi** pomijamy — ruchomy obrazek
    /// rysuje się na gotowym slajdzie, więc zakryłby to, co w prezentacji jest na wierzchu (np. napis na tle).
    /// </summary>
    private static IEnumerable<(string Target, StoredAnimation Rect)> AnimatedPictures(
        ZipArchive zip, XElement slide, string slidePart, double slideWidth, double slideHeight)
    {
        var links = Part(zip, $"{Path.GetDirectoryName(slidePart)!.Replace('\\', '/')}/_rels/{Path.GetFileName(slidePart)}.rels");
        if (links is null)
            yield break;
        var targets = new Dictionary<string, string>();
        foreach (var link in links.Elements(Rel + "Relationship"))
            targets[(string)link.Attribute("Id")!] = (string)link.Attribute("Target")!;
        // tylko dzieci drzewa kształtów: obrazek w grupie ma współrzędne układu grupy, nie slajdu
        var shapes = Shapes(slide);
        for (var i = 0; i < shapes.Count; i++)
        {
            var picture = shapes[i];
            if (picture.Name != P + "pic")
                continue;
            var fill = picture.Element(P + "blipFill");
            var id = (string?)fill?.Element(A + "blip")?.Attribute(R + "embed");
            if (id is null || !targets.TryGetValue(id, out var target))
                continue;
            var part = Media(target);
            if (!part.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || !IsAnimated(zip, part))
                continue;
            if (IsInvisible(picture) || !LooksLikeTheFile(picture, fill!))
                continue;
            if (Rect(picture, slideWidth, slideHeight) is not { } rect)
                continue;
            if (IsCovered(rect, shapes.Skip(i + 1), slideWidth, slideHeight))
                continue;
            yield return (part, rect);
        }
    }

    /// <summary>Kształty leżące wprost na slajdzie, w kolejności rysowania (pierwszy na spodzie).</summary>
    private static List<XElement> Shapes(XElement slide) =>
        slide.Element(P + "cSld")?.Element(P + "spTree")?.Elements().Where(IsShape).ToList() ?? [];

    private static bool IsShape(XElement element) =>
        element.Name == P + "sp" || element.Name == P + "pic" || element.Name == P + "grpSp"
        || element.Name == P + "graphicFrame" || element.Name == P + "cxnSp";

    /// <summary>
    /// Czy na obrazku leży coś narysowanego po nim. Kształt bez podanego miejsca (dziedziczy je z układu slajdu)
    /// liczymy jako zakrywający — nie wiadomo, gdzie wypada, a lepiej zostawić slajd nieruchomy.
    /// </summary>
    private static bool IsCovered(StoredAnimation picture, IEnumerable<XElement> above, double slideWidth, double slideHeight) =>
        above.Any(shape => Rect(shape, slideWidth, slideHeight) is not { } other || Overlaps(picture, other));

    private static bool Overlaps(StoredAnimation one, StoredAnimation other) =>
        one.X < other.X + other.Width && other.X < one.X + one.Width
        && one.Y < other.Y + other.Height && other.Y < one.Y + one.Height;

    /// <summary>Kształt ukryty w okienku zaznaczania — PowerPoint go nie rysuje, więc nie ma go też na slajdzie.</summary>
    private static bool IsInvisible(XElement picture) =>
        (string?)picture.Element(P + "nvPicPr")?.Element(P + "cNvPr")?.Attribute("hidden") == "1";

    /// <summary>
    /// Obraz na slajdzie wygląda tak samo jak sam plik GIF: wypełnia w całości prostokątną ramkę, bez obramowania
    /// i bez zmiany kolorów. Styl obrazu (zaokrąglone narożniki, owal, ramka) i efekty koloru są wypalone w slajdzie,
    /// a ruchoma klatka narysowałaby się bez nich.
    /// </summary>
    private static bool LooksLikeTheFile(XElement picture, XElement fill) =>
        FillsWholeFrame(fill) && HasPlainColours(fill) && IsRectangle(picture) && HasNoBorder(picture);

    /// <summary>Obraz wypełnia całą swoją ramkę w całości: bez przycięcia, kafelkowania i marginesów rozciągnięcia.</summary>
    private static bool FillsWholeFrame(XElement fill)
    {
        if (fill.Element(A + "srcRect") is not null || fill.Element(A + "tile") is not null)
            return false;
        var area = fill.Element(A + "stretch")?.Element(A + "fillRect");
        return area is null || !area.Attributes().Any();
    }

    /// <summary>Kolory obrazu nie są zmieniane (przezroczystość, szarość, duotony, rozjaśnienie, podmiana koloru).</summary>
    private static bool HasPlainColours(XElement fill)
    {
        if (fill.Element(A + "blip") is not { } blip)
            return false;
        // alphaModFix bez wartości nie zmienia nic — PowerPoint wpisuje go przy zwykłych obrazach
        return blip.Elements().All(effect =>
            effect.Name == A + "extLst"
            || (effect.Name == A + "alphaModFix" && effect.Attribute("amt") is null));
    }

    /// <summary>Ramka obrazu jest prostokątna (bez zaokrągleń, owalu i innych kształtów ze „Stylu obrazu”).</summary>
    private static bool IsRectangle(XElement picture)
    {
        var geometry = picture.Element(P + "spPr")?.Element(A + "prstGeom");
        return geometry is null || (string?)geometry.Attribute("prst") == "rect";
    }

    /// <summary>Obraz nie ma obramowania — ruchoma klatka zakryłaby je od środka.</summary>
    private static bool HasNoBorder(XElement picture)
    {
        var line = picture.Element(P + "spPr")?.Element(A + "ln");
        return line is null || line.Element(A + "noFill") is not null;
    }

    /// <summary>
    /// Miejsce obrazu na slajdzie; <c>null</c> = obrócony, odbity albo bez podanego miejsca (dziedziczy je z układu).
    /// </summary>
    private static StoredAnimation? Rect(XElement picture, double slideWidth, double slideHeight)
    {
        var transform = Transform(picture);
        var offset = transform?.Element(A + "off");
        var extent = transform?.Element(A + "ext");
        if (offset is null || extent is null || IsTurned(transform!))
            return null;
        var (x, y) = ((double?)offset.Attribute("x") ?? 0, (double?)offset.Attribute("y") ?? 0);
        var (cx, cy) = ((double?)extent.Attribute("cx") ?? 0, (double?)extent.Attribute("cy") ?? 0);
        return cx > 0 && cy > 0
            ? new StoredAnimation("", x / slideWidth, y / slideHeight, cx / slideWidth, cy / slideHeight)
            : null;
    }

    /// <summary>Położenie kształtu: obraz i zwykły kształt mają je w <c>spPr</c>, grupa w <c>grpSpPr</c>, ramka wprost.</summary>
    private static XElement? Transform(XElement shape) =>
        shape.Element(P + "spPr")?.Element(A + "xfrm")
        ?? shape.Element(P + "grpSpPr")?.Element(A + "xfrm")
        ?? shape.Element(P + "xfrm");

    /// <summary>Obraz obrócony albo odbity — na slajdzie wygląda inaczej niż sam plik GIF.</summary>
    private static bool IsTurned(XElement transform) =>
        transform.Attribute("rot") is not null
        || (string?)transform.Attribute("flipH") == "1"
        || (string?)transform.Attribute("flipV") == "1";

    /// <summary>GIF z więcej niż jedną klatką (zwykły obrazek GIF zostaje częścią PNG slajdu).</summary>
    private static bool IsAnimated(ZipArchive zip, string part)
    {
        if (zip.GetEntry(part) is not { } entry)
            return false;
        using var stream = entry.Open();
        return GifFrames.CountAtLeastTwo(stream);
    }

    private static string Media(string target) => "ppt/" + target.TrimStart('/').Replace("../", "");

    private static void Extract(ZipArchive zip, string part, string file)
    {
        if (zip.GetEntry(part) is not { } entry)
            return;
        using var source = entry.Open();
        using var target = File.Create(file);
        source.CopyTo(target);
    }

    private static XElement? Part(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
            return null;
        using var stream = entry.Open();
        try
        {
            return XDocument.Load(stream).Root;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}

/// <summary>Czy GIF jest animowany — liczy bloki sterujące klatkami, bez dekodowania obrazu.</summary>
internal static class GifFrames
{
    /// <summary>Znacznik bloku klatki w GIF-ie: rozszerzenie sterujące grafiką (<c>21 F9 04</c>).</summary>
    private static readonly byte[] FrameMark = [0x21, 0xF9, 0x04];

    private const int BufferSize = 64 * 1024;

    /// <summary>Plik czytamy porcjami, ze sklejeniem na ich granicy — GIF bywa wielomegabajtowy.</summary>
    public static bool CountAtLeastTwo(Stream stream)
    {
        var overlap = FrameMark.Length - 1;
        var buffer = new byte[BufferSize + overlap];
        var carried = 0;
        var frames = 0;
        int read;
        while ((read = stream.Read(buffer, carried, BufferSize)) > 0)
        {
            var filled = carried + read;
            for (var i = 0; i + FrameMark.Length <= filled; i++)
                if (buffer[i] == FrameMark[0] && buffer[i + 1] == FrameMark[1] && buffer[i + 2] == FrameMark[2] && ++frames > 1)
                    return true;
            carried = Math.Min(overlap, filled);
            Array.Copy(buffer, filled - carried, buffer, 0, carried);
        }
        return false;
    }
}
