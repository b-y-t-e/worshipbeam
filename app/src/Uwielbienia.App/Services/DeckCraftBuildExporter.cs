using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using Uwielbienia.Core.Localization;
using Uwielbienia.Core.Slideshows;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.App.Services;

/// <summary>
/// Slajdy z ruchem elementów przez DeckCraft (<c>deckcraft-cli</c>). DeckCraft czyta animacje z pliku
/// (<c>slide.inspect</c>) i rysuje pojedyncze kształty na przezroczystym tle (<c>shape.visible</c> +
/// <c>file.export … transparentBackground</c>) — z tego powstają warstwy slajdu (<see cref="SlideBuild"/>): tło, grupy
/// nieruchomych kształtów, każdy animowany kształt, jego akapity animowane osobno i warianty koloru. Obrazy stanów po
/// kolejnych kliknięciach składamy z warstw tym samym kodem, którym rysuje je rzutnik (<see cref="SlideBuildRenderer"/>).
/// Wszystkie warstwy jednego pliku rysuje jedno wywołanie DeckCraft (lista poleceń w pliku), więc trwa to chwilę.
/// </summary>
public sealed class DeckCraftBuildExporter : ISlideBuildExporter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>Najwięcej próbek koloru na kształt (wyróżnienia zmieniające kolor szukamy w przebiegu animacji).</summary>
    private const int MaxTintSamples = 4000;

    /// <summary>Najwięcej wariantów koloru jednego kształtu.</summary>
    private const int MaxTints = 3;

    private readonly Lazy<string?> _cli = new(() => ExternalTool.Find(
        [ExternalTool.ProgramFiles("DeckCraft", "deckcraft-cli.exe")], "deckcraft-cli"));

    public bool IsAvailable => _cli.Value is not null;

    /// <summary>Warstwa do narysowania: kształty widoczne na niej (albo jeden kształt z jednym akapitem).</summary>
    private sealed record LayerPlan(string Name, IReadOnlyList<int> Shapes, int? Shape, int? Paragraph, IReadOnlySet<int>? Paragraphs, int Ordinal)
    {
        public List<(string Color, string Name)> Tints { get; } = [];

        public string? TintBase { get; set; }
    }

    /// <summary>Slajd z animacjami: co z niego powstanie.</summary>
    private sealed record SlidePlan(
        int Index,
        int Visible,
        JsonArray Animations,
        IReadOnlyList<BuildShape> Shapes,
        IReadOnlyList<int> AllShapes,
        Timeline Timeline,
        IReadOnlyList<LayerPlan> Layers)
    {
        public string Background => $"s{Index:D3}-tlo.png";
    }

    public async Task<IReadOnlyList<int>?> ExportAsync(string presentation, string outDir, int width, IProgress<string> progress)
    {
        if (_cli.Value is not { } cli)
            return null;
        progress.Report(Tr.T("Slajdy z animacjami (DeckCraft)…"));
        using var info = JsonDocument.Parse(await ExternalTool.RunAsync(cli, ["info", presentation], Timeout).ConfigureAwait(false));
        var size = info.RootElement.GetProperty("slideSize");
        var (slideWidth, slideHeight) = (size[0].GetDouble(), size[1].GetDouble());
        var hidden = info.RootElement.GetProperty("slides").EnumerateArray()
            .Select(s => s.TryGetProperty("hidden", out var h) && h.GetBoolean())
            .ToList();
        var sources = await Task.Run(() => BuildSources.Read(presentation)).ConfigureAwait(false);
        if (sources.Count != hidden.Count || slideWidth <= 0 || slideHeight <= 0)
            return null;

        var work = Path.Combine(outDir, "deckcraft-budowa");
        Directory.CreateDirectory(work);
        try
        {
            var inspected = await InspectAsync(cli, presentation, hidden.Count, work).ConfigureAwait(false);
            var plans = new Dictionary<int, SlidePlan>();
            var visible = 0;
            for (var i = 0; i < hidden.Count; i++)
            {
                if (hidden[i])
                    continue;
                if (inspected[i].GetProperty("animations").GetArrayLength() > 0)
                {
                    // slajd, którego nie umiemy rozłożyć na warstwy (np. animacja kształtu w grupie) — cały plik po staremu
                    if (Plan(i, visible, inspected[i], sources[i], slideWidth, slideHeight) is not { } plan)
                        return null;
                    plans[i] = plan;
                }
                visible++;
            }
            if (plans.Count == 0)
                return null;

            var scale = width / slideWidth;
            await RenderAsync(cli, presentation, hidden, plans, work, scale).ConfigureAwait(false);
            progress.Report(Tr.T("Slajdy z animacjami: warstwy…"));
            return await Task.Run(() => Assemble(hidden, plans, work, outDir, width, scale, slideWidth, slideHeight)).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // zostaje w folderze tymczasowym eksportu
            }
        }
    }

    /// <summary>Opis każdego slajdu (kształty, ramki, animacje) — jedno wywołanie DeckCraft dla całego pliku.</summary>
    private static async Task<List<JsonElement>> InspectAsync(string cli, string presentation, int count, string work)
    {
        var commands = Path.Combine(work, "opis.txt");
        await File.WriteAllLinesAsync(commands, Enumerable.Range(0, count).Select(i => $"slide.inspect={{\"index\":{i}}}")).ConfigureAwait(false);
        var output = await ExternalTool.RunAsync(cli, ["run", "--in", presentation, "--cmd-file", commands, "--print"], Timeout).ConfigureAwait(false);
        var slides = new List<JsonElement>();
        foreach (var line in output.Split('\n'))
        {
            var start = line.IndexOf('{', StringComparison.Ordinal);
            if (!line.StartsWith("slide.inspect", StringComparison.Ordinal) || start < 0)
                continue;
            using var document = JsonDocument.Parse(line[start..]);
            slides.Add(document.RootElement.Clone());
        }
        if (slides.Count != count)
            throw new InvalidDataException(Tr.T("DeckCraft nie opisał wszystkich slajdów"));
        return slides;
    }

    /// <summary>Warstwy slajdu w kolejności rysowania; <c>null</c>, gdy slajdu nie da się tak rozłożyć.</summary>
    private static SlidePlan? Plan(int index, int visible, JsonElement slide, IReadOnlyDictionary<int, BuildSources.SourceShape> sources,
        double slideWidth, double slideHeight)
    {
        var shapes = new List<BuildShape>();
        foreach (var element in slide.GetProperty("shapes").EnumerateArray())
        {
            var id = element.GetProperty("id").GetInt32();
            var box = element.GetProperty("box");
            var fill = element.TryGetProperty("fill", out var f) && f.ValueKind == JsonValueKind.String && Rgba.FromHex(f.GetString()) is not null
                ? f.GetString()
                : null;
            var rotation = element.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : 0;
            shapes.Add(new BuildShape(id, box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), rotation, fill,
                sources.TryGetValue(id, out var source) ? source.Paragraphs : null));
        }
        var animationsJson = JsonNode.Parse(slide.GetProperty("animations").GetRawText())!.AsArray();
        var animations = slide.GetProperty("animations").EnumerateArray().Select(Animation.FromJson).ToList();
        var ids = shapes.Select(s => s.Id).ToHashSet();
        if (animations.Any(a => !ids.Contains(a.Shape)))
            return null;
        var timeline = Timeline.Create(animations, shapes.Select(SlideBuilds.ToTimeline).ToList(), slideWidth, slideHeight);
        if (timeline.IsEmpty)
            return null;

        var animated = timeline.AnimatedShapes().ToHashSet();
        var layers = new List<LayerPlan>();
        var group = new List<int>();
        void CloseGroup()
        {
            if (group.Count == 0)
                return;
            layers.Add(new LayerPlan($"s{index:D3}-w{layers.Count:D2}.png", [.. group], null, null, null, 0));
            group.Clear();
        }
        foreach (var shape in shapes)
        {
            if (sources.TryGetValue(shape.Id, out var source) && source.Hidden)
                continue;
            if (!animated.Contains(shape.Id))
            {
                group.Add(shape.Id);
                continue;
            }
            CloseGroup();
            layers.Add(new LayerPlan($"s{index:D3}-w{layers.Count:D2}.png", [shape.Id], shape.Id, null, null, 0));
            var ordinal = 0;
            foreach (var (paragraph, members) in ParagraphLayers(shape, animations, timeline))
                layers.Add(new LayerPlan($"s{index:D3}-w{layers.Count:D2}.png", [shape.Id], shape.Id, paragraph, members, ordinal++));
        }
        CloseGroup();
        var plan = new SlidePlan(index, visible, animationsJson, shapes, [.. shapes.Select(s => s.Id)], timeline, layers);
        PlanTints(plan);
        return plan;
    }

    /// <summary>
    /// Akapity kształtu animowane osobno: każdy z akapitami, które za nim idą (podpunkty w budowie „po akapicie” —
    /// <see cref="ParagraphBuild.Roles"/>).
    /// </summary>
    private static List<(int Paragraph, IReadOnlySet<int> Members)> ParagraphLayers(BuildShape shape, IReadOnlyList<Animation> animations, Timeline timeline)
    {
        var owners = timeline.Effects.Where(e => e.Shape == shape.Id && e.Paragraph is not null)
            .Select(e => e.Paragraph!.Value).Distinct().Order().ToList();
        if (owners.Count == 0)
            return [];
        var grouped = animations.Any(a => a.Shape == shape.Id && a.Paragraph is null && a.TextBuild == TextBuild.ByParagraph);
        var roles = grouped && shape.Paragraphs is { } paragraphs ? ParagraphBuild.Roles(paragraphs) : [];
        return owners.Select(owner =>
        {
            var members = new HashSet<int> { owner };
            for (var i = 0; i < roles.Count; i++)
                if (roles[i] is BuildRole.Member member && member.Owner == owner)
                    members.Add(i);
            return (owner, (IReadOnlySet<int>)members);
        }).ToList();
    }

    /// <summary>
    /// Kolory, do których zmierza wypełnienie kształtu (wyróżnienia koloru, przyciemnienie po animacji): szukane w przebiegu
    /// animacji; na rzutniku kolor pośredni to przenikanie do wariantu w tym kolorze.
    /// </summary>
    private static void PlanTints(SlidePlan plan)
    {
        var timeline = plan.Timeline;
        foreach (var layer in plan.Layers.Where(l => l.Shape is not null && l.Paragraph is null))
        {
            var id = layer.Shape!.Value;
            var shape = plan.Shapes.First(s => s.Id == id);
            var from = Rgba.FromHex(shape.Fill) ?? Animation.DefaultAccent;
            var seen = new List<Rgba>();
            var samples = 0;
            for (var step = 0; step <= timeline.Steps && samples < MaxTintSamples; step++)
            {
                var end = step < timeline.Steps ? Math.Max(timeline.StepDuration(step), 0) + 1 : 0;
                for (var t = 0.0; t <= end && samples < MaxTintSamples; t += 0.05, samples++)
                    if (timeline.State(id, null, step, t).Tint is { } tint && tint != from && !seen.Contains(tint))
                        seen.Add(tint);
            }
            if (seen.Count == 0)
                continue;
            // najbardziej odległe kolory: pierwszy — najdalej od wyjściowego, następne — najdalej od drogi do już wybranych
            var chosen = new List<Rgba>();
            while (chosen.Count < MaxTints)
            {
                var next = seen
                    .Select(c => (Color: c, Error: chosen.Count == 0
                        ? SlideBuild.Along(from, from, c).Error
                        : chosen.Min(x => SlideBuild.Along(from, x, c).Error)))
                    .MaxBy(x => x.Error);
                if (next.Error < 4)
                    break;
                chosen.Add(next.Color);
            }
            layer.TintBase = from.ToHex();
            for (var i = 0; i < chosen.Count; i++)
                layer.Tints.Add((Opaque(chosen[i]).ToHex(), Path.ChangeExtension(layer.Name, null) + $"-k{i}.png"));
        }
    }

    private static Rgba Opaque(Rgba c) => c with { A = 255 };

    /// <summary>
    /// Rysuje warstwy: kopia bazowa (akapity animowane osobno ukryte) — tła, grupy, kształty, warianty koloru i slajdy bez
    /// animacji; kopie akapitów — po jednej na kolejny akapit kształtu (sam tekst tego akapitu).
    /// </summary>
    private static async Task RenderAsync(string cli, string presentation, IReadOnlyList<bool> hidden, Dictionary<int, SlidePlan> plans, string work, double scale)
    {
        var paragraphLayers = plans.Values.SelectMany(p => p.Layers.Where(l => l.Paragraph is not null).Select(l => (Plan: p, Layer: l))).ToList();
        var basis = presentation;
        if (paragraphLayers.Count > 0)
        {
            basis = Path.Combine(work, "baza" + Path.GetExtension(presentation));
            var edits = paragraphLayers.GroupBy(x => x.Plan.Index).ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<int, BuildSources.ShapeEdit>)g.GroupBy(x => x.Layer.Shape!.Value).ToDictionary(
                    s => s.Key,
                    s => new BuildSources.ShapeEdit(s.SelectMany(x => x.Layer.Paragraphs!).ToHashSet(), TextOnly: false)));
            await Task.Run(() => BuildSources.WriteCopy(presentation, basis, edits)).ConfigureAwait(false);
        }

        var commands = new StringBuilder();
        for (var i = 0; i < hidden.Count; i++)
        {
            if (hidden[i])
                continue;
            if (!plans.TryGetValue(i, out var plan))
            {
                Export(commands, Path.Combine(work, $"s{i:D3}.png"), i, scale, transparent: false);
                continue;
            }
            Command(commands, "slide.go", new JsonObject { ["index"] = i });
            Show(commands, plan.AllShapes, false);
            Export(commands, Path.Combine(work, plan.Background), i, scale, transparent: false);
            HideBackgroundGraphics(commands);
            foreach (var layer in plan.Layers.Where(l => l.Paragraph is null))
            {
                Show(commands, plan.AllShapes, false);
                Show(commands, layer.Shapes, true);
                Export(commands, Path.Combine(work, layer.Name), i, scale, transparent: true);
            }
            // warianty koloru na końcu slajdu — zmiana wypełnienia zostaje w dokumencie
            foreach (var layer in plan.Layers.Where(l => l.Tints.Count > 0))
            {
                Show(commands, plan.AllShapes, false);
                Show(commands, layer.Shapes, true);
                foreach (var (color, name) in layer.Tints)
                {
                    Command(commands, "shape.fill", new JsonObject { ["color"] = color, ["ids"] = new JsonArray(layer.Shape!.Value) });
                    Export(commands, Path.Combine(work, name), i, scale, transparent: true);
                }
            }
        }
        await RunAsync(cli, basis, commands, Path.Combine(work, "baza.txt")).ConfigureAwait(false);

        // kopia na każdy kolejny akapit: w niej kształty pokazują sam tekst swojego n-tego akapitu animowanego osobno
        foreach (var round in paragraphLayers.GroupBy(x => x.Layer.Ordinal).OrderBy(g => g.Key))
        {
            var copy = Path.Combine(work, $"akapity{round.Key}" + Path.GetExtension(presentation));
            var edits = round.GroupBy(x => x.Plan.Index).ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<int, BuildSources.ShapeEdit>)g.ToDictionary(
                    x => x.Layer.Shape!.Value,
                    x => new BuildSources.ShapeEdit(
                        Enumerable.Range(0, x.Plan.Shapes.First(s => s.Id == x.Layer.Shape).Paragraphs?.Count ?? 0)
                            .Where(p => !x.Layer.Paragraphs!.Contains(p)).ToHashSet(),
                        TextOnly: true)));
            await Task.Run(() => BuildSources.WriteCopy(presentation, copy, edits)).ConfigureAwait(false);
            var text = new StringBuilder();
            foreach (var (plan, layer) in round)
            {
                Command(text, "slide.go", new JsonObject { ["index"] = plan.Index });
                HideBackgroundGraphics(text);
                Show(text, plan.AllShapes, false);
                Show(text, layer.Shapes, true);
                Export(text, Path.Combine(work, layer.Name), plan.Index, scale, transparent: true);
            }
            await RunAsync(cli, copy, text, Path.Combine(work, $"akapity{round.Key}.txt")).ConfigureAwait(false);
        }
    }

    private static async Task RunAsync(string cli, string presentation, StringBuilder commands, string file)
    {
        await File.WriteAllTextAsync(file, commands.ToString(), new UTF8Encoding(false)).ConfigureAwait(false);
        await ExternalTool.RunAsync(cli, ["run", "--in", presentation, "--cmd-file", file], Timeout).ConfigureAwait(false);
    }

    private static void Show(StringBuilder commands, IReadOnlyList<int> shapes, bool visible)
    {
        if (shapes.Count > 0)
            Command(commands, "shape.visible", new JsonObject { ["visible"] = visible, ["ids"] = new JsonArray([.. shapes.Select(s => (JsonNode)s)]) });
    }

    // Przezroczysty eksport DeckCraft nadal rysuje kształty wzorca i układu.
    private static void HideBackgroundGraphics(StringBuilder commands) =>
        Command(commands, "design.hideBackgroundGraphics", new JsonObject { ["hide"] = true });

    private static void Export(StringBuilder commands, string file, int slide, double scale, bool transparent)
    {
        var parameters = new JsonObject { ["path"] = file.Replace('\\', '/'), ["slide"] = slide, ["scale"] = Math.Round(scale, 4) };
        if (transparent)
            parameters["transparentBackground"] = true;
        Command(commands, "file.export", parameters);
    }

    private static void Command(StringBuilder commands, string id, JsonObject parameters) =>
        commands.Append(id).Append('=').Append(parameters.ToJsonString()).Append('\n');

    /// <summary>
    /// Wycina warstwy do kształtów (mniej pamięci na rzutniku), składa obrazy stanów i zapisuje opis budów. Zwraca liczbę
    /// obrazów każdego widocznego slajdu.
    /// </summary>
    private static IReadOnlyList<int> Assemble(IReadOnlyList<bool> hidden, Dictionary<int, SlidePlan> plans, string work, string outDir,
        int width, double scale, double slideWidth, double slideHeight)
    {
        var layersDir = Path.Combine(outDir, SlideBuilds.LayersFolder);
        Directory.CreateDirectory(layersDir);
        var counts = new List<int>();
        var stored = new List<SlideBuilds.Stored>();
        var number = 0;
        for (var i = 0; i < hidden.Count; i++)
        {
            if (hidden[i])
                continue;
            if (!plans.TryGetValue(i, out var plan))
            {
                File.Move(Path.Combine(work, $"s{i:D3}.png"), Path.Combine(outDir, $"slide{++number:D3}.png"));
                counts.Add(1);
                continue;
            }
            File.Move(Path.Combine(work, plan.Background), Path.Combine(layersDir, plan.Background));
            var layers = new List<BuildLayer>();
            foreach (var layer in plan.Layers)
                if (Crop(layer, work, layersDir, scale) is { } built)
                    layers.Add(built);

            var images = Enumerable.Range(0, Math.Max(1, plan.Timeline.Steps - (plan.Timeline.Steps > 0 && plan.Timeline.StepIsAuto(0) ? 1 : 0) + 1))
                .Select(_ => $"slide{++number:D3}.png")
                .ToList();
            var build = new SlideBuild(Path.Combine(outDir, images[0]), Path.Combine(layersDir, plan.Background), slideWidth, slideHeight,
                layers.Select(l => l with
                {
                    File = Path.Combine(layersDir, l.File),
                    Tints = l.Tints.Select(t => t with { File = Path.Combine(layersDir, t.File) }).ToList(),
                }).ToList(),
                plan.Shapes, plan.Timeline, images);
            using var pictures = SlideBuildImages.Load(build) ?? throw new InvalidDataException(Tr.T("Nie udało się odczytać warstw slajdu"));
            for (var k = 0; k < images.Count; k++)
                SlideBuildRenderer.Save(build, pictures, build.Rest(k), width, Path.Combine(outDir, images[k]));
            counts.Add(images.Count);
            stored.Add(new SlideBuilds.Stored(plan.Background, slideWidth, slideHeight, images, layers, plan.Shapes, plan.Animations));
        }
        SlideBuilds.Save(outDir, stored);
        return counts;
    }

    /// <summary>
    /// Warstwa przycięta do tego, co na niej narysowano (razem z wariantami koloru — ten sam wycinek); <c>null</c> = pusta.
    /// Miejsce wycinka zapisujemy w punktach slajdu.
    /// </summary>
    private static BuildLayer? Crop(LayerPlan layer, string work, string layersDir, double scale)
    {
        var files = layer.Tints.Select(t => t.Name).Prepend(layer.Name).ToList();
        var bitmaps = files.Select(f => SKBitmap.Decode(Path.Combine(work, f)) ?? throw new InvalidDataException(f)).ToList();
        try
        {
            SKRectI? bounds = null;
            foreach (var bitmap in bitmaps)
                if (Opaque(bitmap) is { } b)
                    bounds = bounds is { } a ? SKRectI.Union(a, b) : b;
            if (bounds is not { } r)
                return null;
            for (var i = 0; i < files.Count; i++)
            {
                using var cropped = new SKBitmap(r.Width, r.Height, bitmaps[i].ColorType, bitmaps[i].AlphaType);
                if (!bitmaps[i].ExtractSubset(cropped, r))
                    throw new InvalidDataException(files[i]);
                using var image = SKImage.FromBitmap(cropped);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.Create(Path.Combine(layersDir, files[i]));
                data.SaveTo(stream);
            }
            return new BuildLayer(layer.Name, r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale, layer.Shape, layer.Paragraph,
                layer.Tints.Select(t => new BuildTint(t.Color, t.Name)).ToList(), layer.Tints.Count > 0 ? layer.TintBase : null);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
        }
    }

    /// <summary>Prostokąt pikseli, które nie są całkiem przezroczyste; <c>null</c> = obraz pusty.</summary>
    private static SKRectI? Opaque(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        int width = bitmap.Width, height = bitmap.Height;
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x].Alpha == 0)
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                bottom = y;
            }
        }
        return right < 0 ? null : new SKRectI(left, top, right + 1, bottom + 1);
    }
}
