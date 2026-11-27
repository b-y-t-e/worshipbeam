using System.IO.Compression;
using System.Text;
using Uwielbienia.Core.Presentation;
using Uwielbienia.Core.Slideshows;

namespace Uwielbienia.Core.Tests;

public sealed class SlideAnimationsTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "uwielbienia-anim-" + Guid.NewGuid().ToString("N"));

    public SlideAnimationsTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        if (Directory.Exists(_temp))
            Directory.Delete(_temp, recursive: true);
    }

    /// <summary>Prezentacja: podane slajdy (XML treści) i obrazy w <c>ppt/media</c>.</summary>
    private string Presentation(IReadOnlyList<string> slides, IReadOnlyDictionary<string, byte[]> media, string? show = null)
    {
        var file = Path.Combine(_temp, "prezentacja.pptx");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        Write(zip, "ppt/presentation.xml", $"""
            <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:sldIdLst>{string.Concat(slides.Select((_, i) => $"""<p:sldId id="{256 + i}" r:id="rId{i + 1}"/>"""))}</p:sldIdLst>
              <p:sldSz cx="9144000" cy="5143500"/>
            </p:presentation>
            """);
        Write(zip, "ppt/_rels/presentation.xml.rels", $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              {string.Concat(slides.Select((_, i) => $"""
                <Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide{i + 1}.xml"/>
                """))}
            </Relationships>
            """);
        for (var i = 0; i < slides.Count; i++)
        {
            Write(zip, $"ppt/slides/slide{i + 1}.xml", $"""
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"{(i == 0 ? show ?? "" : "")}>
                  <p:cSld><p:spTree>{slides[i]}</p:spTree></p:cSld>
                </p:sld>
                """);
            Write(zip, $"ppt/slides/_rels/slide{i + 1}.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  {string.Concat(media.Keys.Select((name, n) => $"""<Relationship Id="rId{n + 1}" Type="image" Target="../media/{name}"/>"""))}
                </Relationships>
                """);
        }
        foreach (var (name, bytes) in media)
        {
            var entry = zip.CreateEntry("ppt/media/" + name);
            using var stream = entry.Open();
            stream.Write(bytes);
        }
        return file;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>GIF o podanej liczbie klatek — liczy się tylko znacznik bloku klatki.</summary>
    private static byte[] Gif(int frames)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("GIF89a"));
        for (var i = 0; i < frames; i++)
            bytes.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
        bytes.Add(0x3B);
        return [.. bytes];
    }

    private static string Picture(string relation, string offset = """<a:off x="457200" y="2571750"/>""",
        string extent = """<a:ext cx="4572000" cy="2571750"/>""", string blipFill = "", string transform = "",
        string blip = "", string shape = "", string hidden = "") => $"""
        <p:pic>
          <p:nvPicPr><p:cNvPr id="1" name="obraz"{hidden}/></p:nvPicPr>
          <p:blipFill><a:blip r:embed="{relation}">{blip}</a:blip>{blipFill}</p:blipFill>
          <p:spPr><a:xfrm{transform}>{offset}{extent}</a:xfrm>{shape}</p:spPr>
        </p:pic>
        """;

    private string Slides(int count)
    {
        var slides = Path.Combine(_temp, "slajdy");
        Directory.CreateDirectory(slides);
        for (var i = 1; i <= count; i++)
            File.WriteAllBytes(Path.Combine(slides, $"slide{i:D3}.png"), [1, 2, 3]);
        return slides;
    }

    [Fact]
    public void Animated_gif_on_a_slide_is_saved_next_to_the_slides_with_its_place()
    {
        var file = Presentation(
            [Picture("rId1"), ""],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(6) });
        var slides = Slides(2);

        SlideAnimations.Export(file, slides);
        var animations = SlideAnimations.Load(slides);

        var picture = Assert.Single(animations["slide001.png"]);
        Assert.Equal(0.05, picture.X, 3);
        Assert.Equal(0.5, picture.Y, 3);
        Assert.Equal(0.5, picture.Width, 3);
        Assert.Equal(0.5, picture.Height, 3);
        Assert.True(File.Exists(picture.File));
        Assert.False(animations.ContainsKey("slide002.png"));
    }

    [Fact]
    public void Gifs_that_look_different_on_the_slide_than_in_the_file_stay_part_of_it()
    {
        var media = new Dictionary<string, byte[]>
        {
            ["zdjecie.gif"] = Gif(1),
            ["obrocony.gif"] = Gif(4),
            ["przyciety.gif"] = Gif(4),
            ["odbity.gif"] = Gif(4),
            ["kafelki.gif"] = Gif(4),
            ["wsuniety.gif"] = Gif(4),
        };
        var file = Presentation(
            [
                Picture("rId1") +
                Picture("rId2", transform: """ rot="5400000" """) +
                Picture("rId3", blipFill: """<a:srcRect l="10000"/>""") +
                Picture("rId4", transform: """ flipH="1" """) +
                Picture("rId5", blipFill: "<a:tile/>") +
                Picture("rId6", blipFill: """<a:stretch><a:fillRect l="5000"/></a:stretch>"""),
            ],
            media);
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Empty(SlideAnimations.Load(slides));
    }

    /// <summary>Kształt (np. pole tekstowe) w podanym miejscu slajdu.</summary>
    private static string Shape(string offset, string extent) => $"""
        <p:sp>
          <p:spPr><a:xfrm>{offset}{extent}</a:xfrm></p:spPr>
        </p:sp>
        """;

    [Fact]
    public void Gif_with_something_drawn_over_it_stays_part_of_the_slide()
    {
        // napis na tle z GIF-a: ruchomy obrazek zakryłby go, bo rysuje się na gotowym slajdzie
        var file = Presentation(
            [Picture("rId1") + Shape("""<a:off x="914400" y="2743200"/>""", """<a:ext cx="1828800" cy="914400"/>""")],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(5) });
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Empty(SlideAnimations.Load(slides));
    }

    [Fact]
    public void Gif_drawn_over_everything_else_keeps_moving()
    {
        // kształt leży pod obrazkiem (jest przed nim w kolejności rysowania) — GIF niczego nie zakrywa
        var file = Presentation(
            [Shape("""<a:off x="914400" y="2743200"/>""", """<a:ext cx="1828800" cy="914400"/>""") + Picture("rId1")],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(5) });
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Single(SlideAnimations.Load(slides)["slide001.png"]);
    }

    [Fact]
    public void Gif_styled_or_recoloured_on_the_slide_stays_part_of_it()
    {
        var media = new Dictionary<string, byte[]>
        {
            ["zaokraglony.gif"] = Gif(4),
            ["w_ramce.gif"] = Gif(4),
            ["przezroczysty.gif"] = Gif(4),
            ["szary.gif"] = Gif(4),
            ["ukryty.gif"] = Gif(4),
        };
        var file = Presentation(
            [
                Picture("rId1", shape: """<a:prstGeom prst="roundRect"><a:avLst/></a:prstGeom>""") +
                Picture("rId2", shape: """<a:ln w="38100"><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill></a:ln>""") +
                Picture("rId3", blip: """<a:alphaModFix amt="50000"/>""") +
                Picture("rId4", blip: "<a:grayscl/>") +
                Picture("rId5", hidden: """ hidden="1" """),
            ],
            media);
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Empty(SlideAnimations.Load(slides));
    }

    [Fact]
    public void Plain_gif_keeps_moving_despite_the_markup_powerpoint_always_writes()
    {
        // PowerPoint zapisuje przy zwykłym obrazie alphaModFix bez wartości, prostokątny kształt i brak obramowania
        var file = Presentation(
            [Picture("rId1", blip: "<a:alphaModFix/>", shape: """<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:ln><a:noFill/></a:ln>""")],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(4) });
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Single(SlideAnimations.Load(slides)["slide001.png"]);
    }

    [Fact]
    public void Gif_inside_a_group_of_shapes_stays_part_of_the_slide()
    {
        // w grupie współrzędne są liczone w jej własnym układzie — nie wiadomo, gdzie obrazek wypadł na slajdzie
        var file = Presentation(
            [$"<p:grpSp>{Picture("rId1")}</p:grpSp>"],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(5) });
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Empty(SlideAnimations.Load(slides));
    }

    [Fact]
    public void Hidden_slides_are_skipped_so_the_numbers_match_the_exported_images()
    {
        // pierwszy slajd ukryty — eksport go pomija, więc GIF z drugiego należy do slide001.png
        var file = Presentation(
            ["", Picture("rId1")],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(3) },
            show: """ show="0" """);
        var slides = Slides(1);

        SlideAnimations.Export(file, slides);

        Assert.Single(SlideAnimations.Load(slides)["slide001.png"]);
    }

    [Fact]
    public void Nothing_is_described_when_the_number_of_slides_does_not_match_the_exported_images()
    {
        var file = Presentation(
            [Picture("rId1")],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(3) });
        // slajdów więcej niż w prezentacji (np. kroki animacji) — nie wiadomo, do którego slajdu należy GIF
        var slides = Slides(3);

        SlideAnimations.Export(file, slides);

        Assert.Empty(SlideAnimations.Load(slides));
        Assert.True(SlideAnimations.IsDescribed(slides));
        // nieużyte GIF-y nie zostają w folderze slajdów
        Assert.Empty(Directory.GetFiles(slides, "*.gif"));
    }

    [Fact]
    public void Slides_without_a_description_are_still_pictures()
    {
        var slides = Slides(1);

        Assert.Empty(SlideAnimations.Load(slides));
        Assert.False(SlideAnimations.IsDescribed(slides));
        Assert.Empty(new SlideshowState(["slide001.png"]).AnimationsOf("slide001.png"));
        Assert.Empty(new ImageSlide("slide001.png", 1).MovingPictures);
    }

    /// <summary>Eksporter, który zapisuje tyle pustych obrazów, ile slajdów ma prezentacja.</summary>
    private sealed class CountingExporter(int slides) : ISlideExporter
    {
        public string Name => "Fake";

        public bool IsAvailable => true;

        public Task ExportAsync(string presentation, string outDir, int width, IProgress<string> progress)
        {
            for (var i = 1; i <= slides; i++)
                File.WriteAllBytes(Path.Combine(outDir, $"slide{i:D3}.png"), [1, 2, 3]);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Moving_pictures_are_found_after_the_exported_folder_is_renamed()
    {
        // eksport idzie do folderu tymczasowego, który dopiero potem staje się folderem slajdów
        var file = Presentation(
            [Picture("rId1")],
            new Dictionary<string, byte[]> { ["kot.gif"] = Gif(4) });
        var temp = Directory.CreateDirectory(Path.Combine(_temp, "slajdy.tmp")).FullName;
        var slides = Path.Combine(_temp, "slajdy");

        await new AnimationStepsExporter(new CountingExporter(1)).ExportAsync(file, temp, 1920, new Progress<string>());
        Directory.Move(temp, slides);

        var picture = Assert.Single(SlideAnimations.Load(slides)["slide001.png"]);
        Assert.Equal(slides, Path.GetDirectoryName(picture.File));
        Assert.True(File.Exists(picture.File));
    }

    [Fact]
    public void The_same_slide_with_the_same_moving_pictures_is_the_same_slide()
    {
        var picture = new SlideAnimation(Path.Combine(_temp, "ruch001.gif"), 0.1, 0.2, 0.3, 0.4);
        var shown = new ImageSlide("slide001.png", 1, [picture]);

        // przebudowany plan daje nowe listy o tej samej treści — ekran nie mruga
        var rebuilt = new ImageSlide("slide001.png", 1, [picture with { }]);
        Assert.Equal(shown, rebuilt);
        Assert.Equal(shown.GetHashCode(), rebuilt.GetHashCode());

        Assert.NotEqual(shown, new ImageSlide("slide001.png", 1));
        Assert.NotEqual(shown, new ImageSlide("slide001.png", 1, [picture with { X = 0.9 }]));
    }
}
