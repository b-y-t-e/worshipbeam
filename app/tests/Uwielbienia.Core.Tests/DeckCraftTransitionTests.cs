using System.IO.Compression;
using System.Text.Json;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.Core.Tests;

/// <summary>
/// Port przejść DeckCraft porównany z oryginałem w Ruście: <c>Fixtures/DeckCraftTransitions</c> zapisuje
/// <c>tools/deckcraft-transition-fixtures</c> (patrz <c>Slideshows/Builds/UPSTREAM-TRANSITIONS.md</c>).
/// </summary>
public class DeckCraftTransitionTests
{
    private const double Tolerance = 1e-6;

    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DeckCraftTransitions");

    private static readonly Lazy<JsonElement> FramesJson = new(() => Load("frames.json"));
    private static readonly Lazy<JsonElement> PptxJson = new(() => Load("pptx.json"));

    private static JsonElement Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, name))).RootElement;

    private static JsonElement Case(int i) => FramesJson.Value.GetProperty("cases")[i];

    public static TheoryData<int, string> Cases()
    {
        var data = new TheoryData<int, string>();
        var i = 0;
        foreach (var c in Load("frames.json").GetProperty("cases").EnumerateArray())
            data.Add(i++, $"{c.GetProperty("kind").GetString()}/{c.GetProperty("option").GetString()}@{c.GetProperty("aspect").GetDouble():0.###}");
        return data;
    }

    public static TheoryData<string> PptxFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Load("pptx.json").GetProperty("files").EnumerateObject())
            data.Add(f.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Frames_match_DeckCraft(int index, string name)
    {
        var c = Case(index);
        var kind = c.GetProperty("kind").GetString()!;
        var option = c.GetProperty("option").GetString()!;
        var aspect = c.GetProperty("aspect").GetDouble();
        Assert.Equal(c.GetProperty("usesSlideParts").GetBoolean(), SlideTransitionFrames.UsesSlideParts(kind, option));
        foreach (var f in c.GetProperty("frames").EnumerateArray())
        {
            var t = f.GetProperty("t").GetDouble();
            var layers = SlideTransitionFrames.Frame(kind, option, t, aspect);
            var at = $"{name} t={t}";
            Assert.True(f.GetProperty("count").GetInt32() == layers.Count, $"{at}: {layers.Count} layers, DeckCraft {f.GetProperty("count").GetInt32()}");
            Assert.True(f.GetProperty("sources").GetString() == Sources(layers), $"{at}: sources");
            var sum = Checksum(layers);
            var expected = f.GetProperty("checksum").GetDouble();
            Assert.True(Math.Abs(sum - expected) <= Tolerance * Math.Max(1.0, Math.Abs(expected)), $"{at}: checksum {sum}, DeckCraft {expected}");
            if (!f.TryGetProperty("layers", out var full))
                continue;
            var i = 0;
            foreach (var l in full.EnumerateArray())
            {
                var layer = layers[i];
                Assert.True(l[0].GetString() == layer.Source.ToString(), $"{at} layer {i}: source {layer.Source}");
                var numbers = Numbers(layer).ToArray();
                for (var k = 0; k < numbers.Length; k++)
                    Assert.True(Math.Abs(numbers[k] - l[k + 1].GetDouble()) <= Tolerance, $"{at} layer {i} number {k}: {numbers[k]}, DeckCraft {l[k + 1].GetDouble()}");
                i++;
            }
        }
    }

    [Fact]
    public void Every_dense_kind_has_a_frame_compared_layer_by_layer()
    {
        var kinds = FramesJson.Value.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("frames").EnumerateArray().Any(f => f.TryGetProperty("layers", out var l) && l.GetArrayLength() > 1))
            .Select(c => c.GetProperty("kind").GetString()!).ToHashSet();
        Assert.All(TransitionCatalog.Kinds.Where(k => k is not ("none" or "cut")), k => Assert.Contains(k, kinds));
    }

    [Fact]
    public void Meshes_match_DeckCraft()
    {
        foreach (var m in FramesJson.Value.GetProperty("meshes").EnumerateArray())
        {
            var layer = SlideTransitionFrames.Frame(m.GetProperty("kind").GetString()!, m.GetProperty("option").GetString()!,
                m.GetProperty("t").GetDouble(), m.GetProperty("aspect").GetDouble())[m.GetProperty("layer").GetInt32()];
            var vertices = TransitionMesh.LayerMesh(layer, out var triangles);
            var expected = m.GetProperty("vertices");
            Assert.Equal(expected.GetArrayLength(), vertices.Count);
            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                double[] actual = [v.Position.X, v.Position.Y, v.Uv.X, v.Uv.Y, v.Opacity];
                for (var k = 0; k < 5; k++)
                    Assert.Equal(expected[i][k].GetDouble(), actual[k], Tolerance);
            }
            Assert.Equal(m.GetProperty("triangles").EnumerateArray().SelectMany(t => t.EnumerateArray().Select(x => x.GetInt32())), triangles);
        }
    }

    [Fact]
    public void Catalog_matches_DeckCraft()
    {
        var catalog = FramesJson.Value.GetProperty("catalog").EnumerateArray().ToList();
        Assert.Equal(catalog.Select(c => c.GetProperty("id").GetString()), TransitionCatalog.Kinds);
        foreach (var c in catalog)
        {
            var id = c.GetProperty("id").GetString()!;
            var info = TransitionCatalog.Find(id)!;
            Assert.Equal(c.GetProperty("label").GetString(), info.Label);
            Assert.Equal(c.GetProperty("category").GetString(), info.Category);
            Assert.Equal(c.GetProperty("durationMs").GetInt32() / 1000.0, TransitionCatalog.DefaultDurationSeconds(id));
            var options = c.GetProperty("options").EnumerateArray().Select(o => o.GetString()!).ToList();
            Assert.Equal(options, info.Options);
            Assert.Equal(c.GetProperty("optionLabels").EnumerateArray().Select(o => o.GetString()), options.Select(o => TransitionCatalog.OptionLabel(id, o)));
        }
    }

    /// <summary>Pada, gdy DeckCraft doda przejście, którego port jeszcze nie zna.</summary>
    [Fact]
    public void Every_fixture_kind_is_in_the_catalog()
    {
        foreach (var c in FramesJson.Value.GetProperty("cases").EnumerateArray())
            Assert.True(TransitionCatalog.IsKnown(c.GetProperty("kind").GetString()!), c.GetProperty("kind").GetString());
    }

    [Theory]
    [MemberData(nameof(PptxFiles))]
    public void File_transitions_match_DeckCraft_reader(string file)
    {
        var expected = PptxJson.Value.GetProperty("files").GetProperty(file).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null
            ? null
            : new FileTransition(e.GetProperty("kind").GetString()!, e.GetProperty("option").GetString()!, e.GetProperty("durationMs").GetInt32() / 1000.0)).ToList();
        Assert.Equal(expected, FileTransitions.Read(Path.Combine(Dir, "pptx", file)));
    }

    [Theory]
    [InlineData("p14", "vortex", "r")]
    [InlineData("future", "fade", "smoothly")]
    public void Alternate_content_inside_a_transition_selects_the_effect(string requires, string kind, string option)
    {
        var file = Path.GetTempFileName();
        try
        {
            using (var zip = new ZipArchive(File.Create(file), ZipArchiveMode.Create))
            {
                void Add(string part, string xml)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(part).Open());
                    writer.Write(xml);
                }

                Add("ppt/presentation.xml", """
                    <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                    xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                      <p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst>
                    </p:presentation>
                    """);
                Add("ppt/_rels/presentation.xml.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"
                                    Target="slides/slide1.xml"/>
                    </Relationships>
                    """);
                Add("ppt/slides/slide1.xml", $"""
                    <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main"
                           xmlns:future="urn:future-powerpoint"
                           xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
                      <p:transition p14:dur="1200">
                        <mc:AlternateContent>
                          <mc:Choice Requires="{requires}"><p14:vortex dir="r"/></mc:Choice>
                          <mc:Fallback><p:fade/></mc:Fallback>
                        </mc:AlternateContent>
                      </p:transition>
                    </p:sld>
                    """);
            }

            Assert.Equal(new FileTransition(kind, option, 1.2), Assert.Single(FileTransitions.Read(file)));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Unreadable_file_has_no_transitions() =>
        Assert.Empty(FileTransitions.Read(Path.Combine(Dir, "frames.json")));

    [Theory]
    [InlineData("fade")]
    [InlineData("push")]
    [InlineData("wipe")]
    [InlineData("cover")]
    public void Reversed_transition_starts_on_the_slide_left_and_ends_on_the_one_come_back_to(string kind)
    {
        // jak test DeckCraft show/tests.rs (#1531): klatki od końca, stary i nowy slajd zamienione
        var start = SlideTransitionFrames.ReverseFrame(kind, "", 0.0, SlideTransitionFrames.DefaultAspect);
        Assert.Equal((TransitionSource.Old, 1.0), (start[^1].Source, start[^1].Alpha));
        var end = SlideTransitionFrames.ReverseFrame(kind, "", 1.0, SlideTransitionFrames.DefaultAspect);
        Assert.All(end, l => Assert.Equal(TransitionSource.New, l.Source));
    }

    [Fact]
    public void Hostile_numbers_give_the_old_slide_or_a_default_aspect()
    {
        Assert.Equal(TransitionSource.Old, Assert.Single(SlideTransitionFrames.Frame("vortex", "l", double.NaN, 1.0)).Source);
        Assert.Equal(SlideTransitionFrames.Frame("cube", "l", 0.4, SlideTransitionFrames.DefaultAspect).Count,
            SlideTransitionFrames.Frame("cube", "l", 0.4, double.PositiveInfinity).Count);
    }

    /// <summary>Źródła warstw jak w <c>frames.json</c>: litera na warstwę, serie jako litera + liczba.</summary>
    private static string Sources(IReadOnlyList<TransitionLayer> layers)
    {
        static char Letter(TransitionSource s) => s switch
        {
            TransitionSource.Old => 'O',
            TransitionSource.New => 'N',
            TransitionSource.Black => 'B',
            TransitionSource.White => 'W',
            TransitionSource.OldBack => 'o',
            TransitionSource.NewBack => 'n',
            TransitionSource.OldContent => 'c',
            _ => 'd',
        };
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < layers.Count;)
        {
            var n = layers.Skip(i).TakeWhile(l => l.Source == layers[i].Source).Count();
            text.Append(Letter(layers[i].Source));
            if (n > 1)
                text.Append(n);
            i += n;
        }
        return text.ToString();
    }

    private static IEnumerable<double> Numbers(TransitionLayer l) =>
        l.Src.Concat(l.Quad.SelectMany(p => new[] { p.X, p.Y })).Concat(l.Uv.SelectMany(p => new[] { p.X, p.Y })).Append(l.Alpha).Concat(l.Fade);

    /// <summary>Σ w(k)·x(k), w(k) = 1 + (k mod 7) / 10 — jak <c>checksum</c> w narzędziu.</summary>
    private static double Checksum(IReadOnlyList<TransitionLayer> layers) =>
        layers.SelectMany(Numbers).Select((x, k) => (1.0 + k % 7 / 10.0) * x).Sum();
}
