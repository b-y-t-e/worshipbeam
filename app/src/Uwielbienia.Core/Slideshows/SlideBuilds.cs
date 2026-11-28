using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.Core.Slideshows;

/// <summary>
/// Warstwa budowy slajdu — wycinek obrazu narysowany przez DeckCraft: tło grupy nieruchomych kształtów, cały animowany
/// kształt albo jeden jego akapit. Miejsce wycinka na slajdzie w punktach (jak w DeckCraft — slajd 960×540 itp.).
/// </summary>
/// <param name="File">Pełna ścieżka pliku PNG (przezroczystego poza kształtem).</param>
/// <param name="Shape">Animowany kształt; <c>null</c> = warstwa nieruchoma.</param>
/// <param name="Paragraph">Akapit kształtu animowany osobno (z akapitami, które za nim idą); <c>null</c> = cały kształt.</param>
/// <param name="Tints">Ten sam wycinek z wypełnieniem w innym kolorze — wyróżnienia zmieniające kolor (przenikanie między nimi).</param>
/// <param name="TintBase">Kolor wypełnienia kształtu, od którego liczy się zmiana koloru.</param>
public sealed record BuildLayer(
    string File,
    double X,
    double Y,
    double Width,
    double Height,
    int? Shape,
    int? Paragraph,
    IReadOnlyList<BuildTint> Tints,
    string? TintBase = null);

/// <summary>Wariant warstwy w kolorze <paramref name="Color"/> (<c>#RRGGBB</c>).</summary>
public sealed record BuildTint(string Color, string File);

/// <summary>Kształt slajdu, do którego odwołują się animacje: ramka i obrót w punktach.</summary>
public sealed record BuildShape(int Id, double X, double Y, double Width, double Height, double Rotation, string? Fill, IReadOnlyList<ParagraphInfo>? Paragraphs)
{
    public Xfrm Box => new(X, Y, Width, Height);
}

/// <summary>Chwila budowy: krok głównej sekwencji i czas od jego początku (jak <see cref="Timeline.State"/>).</summary>
public readonly record struct BuildMoment(int Step, double T);

/// <summary>
/// Jak narysować warstwę w danej chwili: przekształcenie w punktach slajdu, obszar przycięcia (wielokąt w układzie
/// warstwy, przed przekształceniem; <c>null</c> = bez przycięcia), krycie i wariant koloru z udziałem 0..1.
/// </summary>
public sealed record LayerPose(BuildLayer Layer, Matrix3x2 Transform, IReadOnlyList<Vector2>? Clip, double Opacity, BuildTint? Tint, double TintAmount);

/// <summary>
/// Animacje elementów jednego slajdu prezentacji odtwarzane warstwami (<see cref="SlideBuilds"/>). Każdy stan po
/// kolejnym kliknięciu jest osobnym, zwykłym slajdem (<see cref="Images"/>) — telefon, panel i miniatury działają jak
/// dotąd — a rzutnik przy wejściu na taki slajd odgrywa ruch, który do niego prowadzi.
/// </summary>
public sealed class SlideBuild
{
    /// <summary>Kolor, od którego DeckCraft liczy zmianę koloru kształtu bez jednolitego wypełnienia.</summary>
    private static readonly Rgba FallbackFill = Animation.DefaultAccent;

    /// <summary>Krok czasu, po którym na pewno skończył się efekt bez czasu trwania (np. „Pojawianie”).</summary>
    private const double AfterEnd = 0.001;

    private readonly Dictionary<int, BuildShape> _shapes;

    public SlideBuild(string key, string background, double slideWidth, double slideHeight, IReadOnlyList<BuildLayer> layers,
        IReadOnlyList<BuildShape> shapes, Timeline timeline, IReadOnlyList<string> images)
    {
        Key = key;
        Background = background;
        SlideWidth = slideWidth;
        SlideHeight = slideHeight;
        Layers = layers;
        Shapes = shapes;
        Timeline = timeline;
        Images = images;
        _shapes = shapes.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Niepowtarzalny klucz (folder slajdów + slajd) — do porównań i pamięci podręcznej obrazów warstw.</summary>
    public string Key { get; }

    /// <summary>Pełna ścieżka tła slajdu (wzór slajdu i układu, bez kształtów slajdu).</summary>
    public string Background { get; }

    public double SlideWidth { get; }

    public double SlideHeight { get; }

    /// <summary>Warstwy od spodu (nad tłem).</summary>
    public IReadOnlyList<BuildLayer> Layers { get; }

    public IReadOnlyList<BuildShape> Shapes { get; }

    public Timeline Timeline { get; }

    /// <summary>Nazwy plików slajdów: stan przed pierwszym kliknięciem, po pierwszym, …</summary>
    public IReadOnlyList<string> Images { get; }

    /// <summary>Pierwszy krok rusza sam przy wejściu na slajd (efekty „z poprzednim” / „po poprzednim” na początku).</summary>
    public int AutoSteps => Timeline.Steps > 0 && Timeline.StepIsAuto(0) ? 1 : 0;

    /// <summary>Krok, który prowadzi do slajdu <paramref name="image"/>; <c>null</c> = nic się nie rusza (początek bez efektów automatycznych).</summary>
    public int? PlayStep(int image)
    {
        var step = AutoSteps + image - 1;
        return step >= 0 && step < Timeline.Steps ? step : null;
    }

    /// <summary>Stan po zakończeniu ruchu, który prowadzi do slajdu <paramref name="image"/> — tak wygląda jego obraz.</summary>
    public BuildMoment Rest(int image) =>
        PlayStep(image) is { } step ? new BuildMoment(step, End(step)) : new BuildMoment(0, 0);

    /// <summary>Koniec kroku: wszystkie jego efekty skończone (poza powtarzanymi do następnego kliknięcia).</summary>
    public double End(int step) => Math.Max(0, Timeline.StepDuration(step)) + AfterEnd;

    /// <summary>Warstwy widoczne w chwili <paramref name="moment"/>, od spodu.</summary>
    public IReadOnlyList<LayerPose> Poses(BuildMoment moment)
    {
        var poses = new List<LayerPose>(Layers.Count);
        foreach (var layer in Layers)
            if (Pose(layer, moment) is { } pose)
                poses.Add(pose);
        return poses;
    }

    private LayerPose? Pose(BuildLayer layer, BuildMoment moment)
    {
        if (layer.Shape is not { } id || !_shapes.TryGetValue(id, out var shape))
            return new LayerPose(layer, Matrix3x2.Identity, null, 1, null, 0);
        var state = Timeline.State(id, null, moment.Step, moment.T);
        if (!state.Visible)
            return null;
        var opacity = state.Opacity;
        var transform = ShapeTransform(state, shape);
        if (layer.Paragraph is { } paragraph)
        {
            // akapit porusza się względem swojego kształtu (DeckCraft: widoczność, krycie i przesunięcie akapitu)
            var own = Timeline.State(id, paragraph, moment.Step, moment.T);
            if (!own.Visible)
                return null;
            opacity *= own.Opacity;
            var offset = Vector2.Transform(new Vector2((float)own.OffsetX, (float)own.OffsetY), Matrix3x2.CreateRotation(Radians(shape.Rotation)));
            transform = Matrix3x2.CreateTranslation(offset) * transform;
        }
        if (opacity <= 0)
            return null;
        var (tint, amount) = layer.Paragraph is null ? Tint(layer, state.Tint) : (null, 0);
        return new LayerPose(layer, transform, Clip(state.Clip, shape), opacity, tint, amount);
    }

    /// <summary>
    /// Przesunięcie, skala i obrót wokół środka kształtu (skala w układzie obróconego kształtu), jak w rendererze DeckCraft.
    /// </summary>
    private static Matrix3x2 ShapeTransform(AnimState state, BuildShape shape)
    {
        if (state.ScaleX == 1 && state.ScaleY == 1 && state.Rotate == 0 && state.OffsetX == 0 && state.OffsetY == 0)
            return Matrix3x2.Identity;
        var centre = new Vector2((float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2));
        var turn = Radians(shape.Rotation);
        return Matrix3x2.CreateTranslation(-centre)
            * Matrix3x2.CreateRotation(-turn)
            * Matrix3x2.CreateScale((float)state.ScaleX, (float)state.ScaleY)
            * Matrix3x2.CreateRotation(turn + Radians(state.Rotate))
            * Matrix3x2.CreateTranslation(centre)
            * Matrix3x2.CreateTranslation((float)state.OffsetX, (float)state.OffsetY);
    }

    /// <summary>Odsłonięta część kształtu (ułamki jego ramki, w układzie obróconego kształtu) jako wielokąt na slajdzie.</summary>
    private static Vector2[]? Clip(ClipRect? clip, BuildShape shape)
    {
        if (clip is not { } c)
            return null;
        var rotate = Matrix3x2.CreateRotation(Radians(shape.Rotation),
            new Vector2((float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2)));
        Vector2 At(double u, double v) =>
            Vector2.Transform(new Vector2((float)(shape.X + u * shape.Width), (float)(shape.Y + v * shape.Height)), rotate);
        return [At(c.L, c.T), At(c.R, c.T), At(c.R, c.B), At(c.L, c.B)];
    }

    /// <summary>
    /// Kolor kształtu jako przenikanie do jednego z wariantów: ten, na którego drodze od koloru wyjściowego leży
    /// <paramref name="tint"/>, i jak daleko na niej (dla zwykłego wypełnienia to dokładnie ten sam kolor).
    /// </summary>
    private static (BuildTint? Tint, double Amount) Tint(BuildLayer layer, Rgba? tint)
    {
        if (tint is not { } target || layer.Tints.Count == 0)
            return (null, 0);
        var from = Rgba.FromHex(layer.TintBase) ?? FallbackFill;
        BuildTint? best = null;
        double bestAmount = 0, bestError = double.MaxValue;
        foreach (var variant in layer.Tints)
        {
            if (Rgba.FromHex(variant.Color) is not { } to)
                continue;
            var (amount, error) = Along(from, to, target);
            if (error < bestError)
                (best, bestAmount, bestError) = (variant, amount, error);
        }
        return bestAmount > 0.001 ? (best, bestAmount) : (null, 0);
    }

    /// <summary>Rzut koloru <paramref name="c"/> na odcinek od <paramref name="a"/> do <paramref name="b"/>: udział i odległość od odcinka.</summary>
    public static (double Amount, double Error) Along(Rgba a, Rgba b, Rgba c)
    {
        double dr = b.R - a.R, dg = b.G - a.G, db = b.B - a.B;
        double cr = c.R - a.R, cg = c.G - a.G, cb = c.B - a.B;
        var length = dr * dr + dg * dg + db * db;
        var amount = length > 0 ? Math.Clamp((cr * dr + cg * dg + cb * db) / length, 0, 1) : 0;
        double er = cr - amount * dr, eg = cg - amount * dg, eb = cb - amount * db;
        return (amount, Math.Sqrt(er * er + eg * eg + eb * eb));
    }

    private static float Radians(double degrees) => (float)(degrees * Math.PI / 180);
}

/// <summary>Slajd prezentacji, który jest jednym ze stanów budowy (<see cref="SlideBuild.Images"/>).</summary>
/// <param name="Image">Który stan: 0 = przed pierwszym kliknięciem.</param>
public sealed record SlideBuildFrame(SlideBuild Build, int Image)
{
    /// <summary>Ruch prowadzący do tego slajdu; <c>null</c> = slajd stoi.</summary>
    public int? PlayStep => Build.PlayStep(Image);

    public bool Equals(SlideBuildFrame? other) => other is not null && other.Build.Key == Build.Key && other.Image == Image;

    public override int GetHashCode() => HashCode.Combine(Build.Key, Image);
}

/// <summary>
/// Zapis i odczyt budów slajdów (<see cref="SlideBuild"/>) obok slajdów: <see cref="MapFile"/> i warstwy w
/// <see cref="LayersFolder"/>. Zapisane są nazwy plików (folder slajdów jest przenoszony i kopiowany) i animacje tak, jak
/// podał je DeckCraft — oś czasu powstaje przy odczycie z kodu przeniesionego z DeckCraft (<see cref="Timeline"/>).
/// </summary>
public static class SlideBuilds
{
    public const string MapFile = "budowa.json";

    public const string LayersFolder = "warstwy";

    private const int Version = 1;

    /// <summary>Zapis jednej budowy: nazwy plików względem folderu warstw i animacje jako JSON z DeckCraft.</summary>
    public sealed record Stored(
        string Background,
        double SlideWidth,
        double SlideHeight,
        IReadOnlyList<string> Images,
        IReadOnlyList<BuildLayer> Layers,
        IReadOnlyList<BuildShape> Shapes,
        JsonArray Animations);

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Save(string slidesDir, IReadOnlyList<Stored> builds)
    {
        var root = new JsonObject
        {
            ["version"] = Version,
            ["builds"] = new JsonArray(builds.Select(b => (JsonNode)new JsonObject
            {
                ["background"] = b.Background,
                ["slideWidth"] = b.SlideWidth,
                ["slideHeight"] = b.SlideHeight,
                ["images"] = JsonSerializer.SerializeToNode(b.Images, Options),
                ["layers"] = JsonSerializer.SerializeToNode(b.Layers, Options),
                ["shapes"] = JsonSerializer.SerializeToNode(b.Shapes, Options),
                ["animations"] = b.Animations.DeepClone(),
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(slidesDir, MapFile), root.ToJsonString());
    }

    /// <summary>Budowy slajdów (nazwa pliku slajdu → który to stan której budowy); pusty, gdy ich nie ma albo nie da się ich odczytać.</summary>
    public static IReadOnlyDictionary<string, SlideBuildFrame> Load(string slidesDir)
    {
        var frames = new Dictionary<string, SlideBuildFrame>(StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(slidesDir, MapFile);
        if (!File.Exists(file))
            return frames;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != Version)
                return frames;
            var layersDir = Path.Combine(slidesDir, LayersFolder);
            foreach (var element in document.RootElement.GetProperty("builds").EnumerateArray())
            {
                if (Read(element, slidesDir, layersDir) is not { } build)
                    continue;
                for (var i = 0; i < build.Images.Count; i++)
                    frames[build.Images[i]] = new SlideBuildFrame(build, i);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                       or KeyNotFoundException or FormatException)
        {
            // nieczytelny opis — slajdy zostają nieruchome (każdy stan to i tak gotowy obraz)
            frames.Clear();
        }
        return frames;
    }

    private static SlideBuild? Read(JsonElement element, string slidesDir, string layersDir)
    {
        var background = Path.Combine(layersDir, element.GetProperty("background").GetString()!);
        var images = element.GetProperty("images").Deserialize<List<string>>(Options) ?? [];
        var layers = (element.GetProperty("layers").Deserialize<List<BuildLayer>>(Options) ?? [])
            .Select(l => l with
            {
                File = Path.Combine(layersDir, l.File),
                Tints = (l.Tints ?? []).Select(t => t with { File = Path.Combine(layersDir, t.File) }).ToList(),
            })
            .ToList();
        var shapes = element.GetProperty("shapes").Deserialize<List<BuildShape>>(Options) ?? [];
        var animations = element.GetProperty("animations").EnumerateArray().Select(Animation.FromJson).ToList();
        if (images.Count == 0 || !File.Exists(background) || layers.Any(l => !File.Exists(l.File)))
            return null;
        var width = element.GetProperty("slideWidth").GetDouble();
        var height = element.GetProperty("slideHeight").GetDouble();
        var timeline = Timeline.Create(animations, shapes.Select(ToTimeline).ToList(), width, height);
        return new SlideBuild(Path.Combine(slidesDir, images[0]), background, width, height, layers, shapes, timeline, images);
    }

    public static TimelineShape ToTimeline(BuildShape shape) =>
        new(shape.Id, shape.Box, Rgba.FromHex(shape.Fill), shape.Paragraphs);
}
