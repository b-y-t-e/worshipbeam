using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Uwielbienia.Core.Slideshows;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.Core.Tests;

/// <summary>Ruch elementów slajdu odtwarzany warstwami: zapis, stany, położenie warstw, kopie z ukrytymi akapitami, eksport.</summary>
public sealed class SlideBuildsTests : IDisposable
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private readonly string _temp = Directory.CreateTempSubdirectory("budowa-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static readonly BuildShape Star = new(7, 100, 150, 200, 100, 0, "#2E6FD8", null);

    private static JsonObject Effect(string effect, string start = "onClick", string cls = "entrance", int duration = 1000, string option = "", int shape = 7, int? paragraph = null) => new()
    {
        ["shape"] = shape, ["class"] = cls, ["effect"] = effect, ["option"] = option, ["start"] = start,
        ["durationMs"] = duration, ["delayMs"] = 0, ["repeat"] = 1, ["autoReverse"] = false, ["rewind"] = false,
        ["smoothStart"] = 0.0, ["smoothEnd"] = 0.0, ["bounceEnd"] = 0.0, ["amount"] = null, ["color"] = null, ["path"] = null,
        ["paragraph"] = paragraph, ["textBuild"] = "asOne", ["trigger"] = null, ["after"] = null,
    };

    private SlideBuild Build(BuildShape shape, params JsonObject[] effects)
    {
        var layer = new BuildLayer(Path.Combine(_temp, "w.png"), shape.X, shape.Y, shape.Width, shape.Height, shape.Id, null, []);
        var animations = effects.Select(e => Animation.FromJson(System.Text.Json.JsonDocument.Parse(e.ToJsonString()).RootElement)).ToList();
        var timeline = Timeline.Create(animations, [SlideBuilds.ToTimeline(shape)], 960, 540);
        var images = Enumerable.Range(1, timeline.Steps - (timeline.StepIsAuto(0) ? 1 : 0) + 1).Select(i => $"slide{i:D3}.png").ToList();
        return new SlideBuild("k", Path.Combine(_temp, "tlo.png"), 960, 540, [layer], [shape], timeline, images);
    }

    [Fact]
    public void Images_are_states_before_and_after_each_click()
    {
        var build = Build(Star, Effect("fade"), Effect("spin", cls: "emphasis"));

        Assert.Equal(3, build.Images.Count);
        Assert.Null(build.PlayStep(0));                 // przed pierwszym kliknięciem nic się nie rusza
        Assert.Equal(0, build.PlayStep(1));
        Assert.Equal(1, build.PlayStep(2));
        Assert.Empty(build.Poses(build.Rest(0)));       // wejście jeszcze nie zagrało — kształtu nie widać
        Assert.Single(build.Poses(build.Rest(1)));
    }

    [Fact]
    public void Automatic_first_step_plays_when_the_slide_appears()
    {
        var build = Build(Star, Effect("fade", start: "withPrevious"), Effect("wipe", option: "l"));

        Assert.Equal(1, build.AutoSteps);
        Assert.Equal(2, build.Images.Count);
        Assert.Equal(0, build.PlayStep(0));             // wejście na slajd odgrywa krok automatyczny
        Assert.Equal(1, build.PlayStep(1));
    }

    [Fact]
    public void Fade_changes_only_the_opacity_of_the_layer()
    {
        var build = Build(Star, Effect("fade"));

        var half = Assert.Single(build.Poses(new BuildMoment(0, 0.5)));

        Assert.InRange(half.Opacity, 0.01, 0.99);
        Assert.Equal(Matrix3x2.Identity, half.Transform);
        Assert.Null(half.Clip);
    }

    [Fact]
    public void Fly_in_from_the_left_starts_outside_the_slide()
    {
        var build = Build(Star, Effect("fly", option: "l"));

        var start = Assert.Single(build.Poses(new BuildMoment(0, 0.0001)));
        var end = Assert.Single(build.Poses(build.Rest(1)));

        var right = Vector2.Transform(new Vector2((float)(Star.X + Star.Width), (float)Star.Y), start.Transform);
        Assert.True(right.X <= 1, $"prawy brzeg na {right.X}");
        Assert.Equal(Matrix3x2.Identity, end.Transform);
    }

    [Fact]
    public void Wipe_clips_to_the_revealed_part_of_the_shape()
    {
        var build = Build(Star, Effect("wipe", option: "l"));

        var pose = Assert.Single(build.Poses(new BuildMoment(0, 0.5)));

        Assert.NotNull(pose.Clip);
        var xs = pose.Clip!.Select(p => p.X).ToList();
        Assert.Equal(Star.X, xs.Min(), 3);
        Assert.InRange(xs.Max(), Star.X + 1, Star.X + Star.Width - 1);
    }

    [Fact]
    public void Colour_is_a_blend_towards_the_tinted_variant()
    {
        var (amount, error) = SlideBuild.Along(Rgba.Rgb(0, 0, 0), Rgba.Rgb(200, 100, 0), Rgba.Rgb(100, 50, 0));

        Assert.Equal(0.5, amount, 3);
        Assert.Equal(0, error, 3);
    }

    [Fact]
    public void Builds_are_saved_and_read_back_with_full_paths()
    {
        var layers = Path.Combine(_temp, SlideBuilds.LayersFolder);
        Directory.CreateDirectory(layers);
        File.WriteAllBytes(Path.Combine(layers, "tlo.png"), [1]);
        File.WriteAllBytes(Path.Combine(layers, "w.png"), [1]);
        File.WriteAllBytes(Path.Combine(layers, "w-k0.png"), [1]);
        var layer = new BuildLayer("w.png", 1, 2, 3, 4, 7, null, [new BuildTint("#FF0000", "w-k0.png")], "#2E6FD8");
        var animations = new JsonArray(Effect("fade"), Effect("fade", cls: "exit"));

        SlideBuilds.Save(_temp, [new SlideBuilds.Stored("tlo.png", 960, 540, ["slide004.png", "slide005.png", "slide006.png"], [layer], [Star], animations)]);
        var frames = SlideBuilds.Load(_temp);

        Assert.Equal(3, frames.Count);
        var frame = frames["slide005.png"];
        Assert.Equal(1, frame.Image);
        Assert.Equal(Path.Combine(layers, "w.png"), frame.Build.Layers[0].File);
        Assert.Equal(Path.Combine(layers, "w-k0.png"), frame.Build.Layers[0].Tints[0].File);
        Assert.Equal(2, frame.Build.Timeline.Steps);
        Assert.Equal(frames["slide004.png"].Build, frame.Build);
    }

    [Fact]
    public void Missing_layer_file_drops_the_build()
    {
        var layers = Path.Combine(_temp, SlideBuilds.LayersFolder);
        Directory.CreateDirectory(layers);
        File.WriteAllBytes(Path.Combine(layers, "tlo.png"), [1]);
        var layer = new BuildLayer("brak.png", 1, 2, 3, 4, 7, null, []);

        SlideBuilds.Save(_temp, [new SlideBuilds.Stored("tlo.png", 960, 540, ["slide001.png", "slide002.png"], [layer], [Star], new JsonArray(Effect("fade")))]);

        Assert.Empty(SlideBuilds.Load(_temp));
    }

    private const string TextSlide = """
        <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><p:cSld><p:spTree>
          <p:nvGrpSpPr><p:cNvPr id="1" name=""/></p:nvGrpSpPr>
          <p:sp><p:nvSpPr><p:cNvPr id="2" name="Tytuł" hidden="1"/></p:nvSpPr><p:spPr/></p:sp>
          <p:sp><p:nvSpPr><p:cNvPr id="3" name="Punkty"/></p:nvSpPr>
            <p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="10" cy="10"/></a:xfrm><a:prstGeom prst="rect"/><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill><a:ln><a:solidFill><a:srgbClr val="000000"/></a:solidFill></a:ln></p:spPr>
            <p:txBody><a:p><a:r><a:t>pierwszy</a:t></a:r></a:p><a:p><a:pPr lvl="1"/><a:r><a:t>podpunkt</a:t></a:r></a:p><a:p><a:r><a:t> </a:t></a:r></a:p><a:p><a:r><a:t>drugi</a:t></a:r></a:p></p:txBody></p:sp>
        </p:spTree></p:cSld></p:sld>
        """;

    private string TextPresentation()
    {
        var file = Path.Combine(_temp, "tekst.pptx");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        void Add(string part, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(part).Open());
            writer.Write(xml);
        }
        Add("ppt/presentation.xml", """<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst></p:presentation>""");
        Add("ppt/_rels/presentation.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/></Relationships>""");
        Add("ppt/slides/slide1.xml", TextSlide);
        return file;
    }

    [Fact]
    public void Sources_give_hidden_shapes_and_paragraph_levels()
    {
        var slide = Assert.Single(BuildSources.Read(TextPresentation()));

        Assert.True(slide[2].Hidden);
        Assert.False(slide[3].Hidden);
        Assert.Equal([new ParagraphInfo(0, true), new ParagraphInfo(1, true), new ParagraphInfo(0, false), new ParagraphInfo(0, true)], slide[3].Paragraphs);
    }

    [Fact]
    public void Copy_hides_paragraphs_and_can_leave_the_text_alone()
    {
        var source = TextPresentation();
        var copy = Path.Combine(_temp, "kopia.pptx");

        BuildSources.WriteCopy(source, copy, new Dictionary<int, IReadOnlyDictionary<int, BuildSources.ShapeEdit>>
        {
            [0] = new Dictionary<int, BuildSources.ShapeEdit> { [3] = new(new HashSet<int> { 0, 1 }, TextOnly: true) },
        });

        using var zip = ZipFile.OpenRead(copy);
        using var stream = zip.GetEntry("ppt/slides/slide1.xml")!.Open();
        var shape = XDocument.Load(stream).Descendants(P + "sp").Single(s => s.Descendants(P + "cNvPr").First().Attribute("id")!.Value == "3");
        var paragraphs = shape.Descendants(A + "p").ToList();
        Assert.NotNull(paragraphs[0].Descendants(A + "rPr").Single().Element(A + "noFill"));
        Assert.NotNull(paragraphs[1].Descendants(A + "rPr").Single().Element(A + "noFill"));
        Assert.Empty(paragraphs[3].Descendants(A + "rPr"));
        var properties = shape.Element(P + "spPr")!;
        Assert.Equal(["xfrm", "prstGeom", "noFill", "ln", "effectLst"], properties.Elements().Select(e => e.Name.LocalName));
        Assert.NotNull(properties.Element(A + "ln")!.Element(A + "noFill"));
    }

    private sealed class FakeBuilds(IReadOnlyList<int>? result, bool fail = false) : ISlideBuildExporter
    {
        public bool IsAvailable => true;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<int>?> ExportAsync(string presentation, string outDir, int width, IProgress<string> progress)
        {
            Calls++;
            if (fail)
                throw new InvalidOperationException("nie");
            File.WriteAllBytes(Path.Combine(outDir, "slide001.png"), [1]);
            return Task.FromResult(result);
        }
    }

    private sealed class CountingExporter : ISlideExporter
    {
        public string Name => "zwykły";

        public bool IsAvailable => true;

        public int Calls { get; private set; }

        public Task ExportAsync(string presentation, string outDir, int width, IProgress<string> progress)
        {
            Calls++;
            File.WriteAllBytes(Path.Combine(outDir, "slide001.png"), [2]);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Presentation_with_animations_is_exported_with_builds()
    {
        var file = Path.Combine(_temp, "anim.pptx");
        AnimationStepsTests.Write(file, animated: true);
        var outDir = Directory.CreateDirectory(Path.Combine(_temp, "out")).FullName;
        var builds = new FakeBuilds([3]);
        var inner = new CountingExporter();
        var exporter = new AnimationStepsExporter(inner, builds);

        await exporter.ExportAsync(file, outDir, 1920, new Progress<string>());

        Assert.Equal(1, builds.Calls);
        Assert.Equal(0, inner.Calls);
        Assert.Equal(AnimationStepsExporter.WithBuilds, exporter.Current);
        Assert.True(exporter.IsOutdated(file, "kroki-2"));
    }

    [Fact]
    public async Task Failed_builds_fall_back_to_steps()
    {
        var file = Path.Combine(_temp, "anim.pptx");
        AnimationStepsTests.Write(file, animated: true);
        var outDir = Directory.CreateDirectory(Path.Combine(_temp, "out")).FullName;
        var errors = new List<Exception>();
        var inner = new CountingExporter();
        var exporter = new AnimationStepsExporter(inner, new FakeBuilds(null, fail: true)) { ErrorNote = errors.Add };

        await exporter.ExportAsync(file, outDir, 1920, new Progress<string>());

        Assert.Single(errors);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Presentation_without_animations_skips_builds()
    {
        var file = Path.Combine(_temp, "zwykla.pptx");
        AnimationStepsTests.Write(file, animated: false);
        var outDir = Directory.CreateDirectory(Path.Combine(_temp, "out")).FullName;
        var builds = new FakeBuilds([1]);
        var exporter = new AnimationStepsExporter(new CountingExporter(), builds);

        await exporter.ExportAsync(file, outDir, 1920, new Progress<string>());

        Assert.Equal(0, builds.Calls);
        Assert.False(exporter.IsOutdated(file, "kroki-2"));
    }

    [Fact]
    public void File_transitions_go_to_the_first_image_of_each_visible_slide()
    {
        var deck = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DeckCraftTransitions", "pptx", "handwritten.pptx");
        var all = FileTransitions.Read(deck);
        var visible = all.Where((_, i) => !IsHidden(deck, i)).ToList();
        var counts = visible.Select((_, i) => i == 0 ? 2 : 1).ToList();
        var outDir = Directory.CreateDirectory(Path.Combine(_temp, "przejscia")).FullName;
        for (var n = 1; n <= counts.Sum(); n++)
            File.WriteAllBytes(Path.Combine(outDir, $"slide{n:D3}.png"), [1]);

        SlideFileTransitions.Export(deck, outDir, counts);
        var map = SlideFileTransitions.Load(outDir);

        Assert.True(visible.Count < all.Count, "wzorzec ma slajd ukryty");
        Assert.Equal(visible[0], map.GetValueOrDefault("slide001.png"));
        Assert.False(map.ContainsKey("slide002.png"));          // drugi obraz pierwszego slajdu (krok) — bez przejścia
        Assert.Equal(visible[1], map.GetValueOrDefault("slide003.png"));
        // slajd z samym czasem / dźwiękiem przejścia (rodzaj „none”) przechodzi jak w planie — nie ma go w opisie
        Assert.Contains(visible, t => t is { Kind: "none" });
        Assert.DoesNotContain(map.Values, t => t.Kind == "none");
        Assert.Equal(visible.Count(t => t is not null and not { Kind: "none" }), map.Count);
    }

    private static bool IsHidden(string deck, int index)
    {
        using var zip = ZipFile.OpenRead(deck);
        var presentation = XDocument.Load(zip.GetEntry("ppt/presentation.xml")!.Open());
        var rels = XDocument.Load(zip.GetEntry("ppt/_rels/presentation.xml.rels")!.Open());
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var id = presentation.Descendants(P + "sldId").ElementAt(index).Attribute(r + "id")!.Value;
        var target = rels.Root!.Elements().First(e => e.Attribute("Id")!.Value == id).Attribute("Target")!.Value;
        var slide = XDocument.Load(zip.GetEntry("ppt/" + target.TrimStart('/').Replace("ppt/", ""))!.Open());
        return slide.Root!.Attribute("show")?.Value is "0" or "false";
    }
}
