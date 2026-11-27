using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Uwielbienia.Core.Plans;
using Uwielbienia.Core.Presentation;
using Uwielbienia.Core.Slideshows;

namespace Uwielbienia.Core.Tests;

/// <summary>Prezentacje w planie: kopia plików, slajdy przygotowywane w tle, pozycja planu i jej zapis.</summary>
public sealed class PresentationTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "uwielbienia-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeExporter _exporter = new();
    private readonly SlideshowLibrary _library;

    public PresentationTests()
    {
        Directory.CreateDirectory(_temp);
        _library = new SlideshowLibrary(Path.Combine(_temp, "prezentacje"), _exporter, _exporter, new ImmediateDispatcher());
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private sealed class FakeExporter : ISlideExporter, ISlideExportVersion
    {
        public string Current => "v2";

        /// <summary>Slajdy starszej wersji są nieaktualne.</summary>
        public bool Outdated { get; set; }

        public bool IsOutdated(string presentation, string exported) => Outdated;

        public string Name => "Fake";

        public bool IsAvailable => true;

        public TaskCompletionSource Release { get; set; } = new();

        public int Slides { get; set; } = 3;

        public string? Error { get; set; }

        public async Task ExportAsync(string presentation, string outDir, int width, IProgress<string> progress)
        {
            progress.Report("Slajd 1 z 3…");
            await Release.Task;
            if (Error is not null)
                throw new InvalidOperationException(Error);
            for (var i = 1; i <= Slides; i++)
                File.WriteAllText(Path.Combine(outDir, $"slide{i:D3}.png"), "");
        }
    }

    private string Source(string name)
    {
        var file = Path.Combine(_temp, name);
        File.WriteAllText(file, "x");
        return file;
    }

    [Fact]
    public async Task Images_are_copied_and_become_slides_in_natural_order()
    {
        var imported = await _library.ImportAsync([Source("slajd10.png"), Source("slajd2.jpg"), Source("notatki.txt")]);

        var state = _library.Load(imported.Folder);

        Assert.Equal("slajd2", imported.Title);
        Assert.Null(state.Note);
        Assert.Equal(["001.jpg", "002.png"], state.Slides.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Presentation_slides_are_prepared_in_background_once()
    {
        var imported = await _library.ImportAsync([Source("Moc Słowa.pptx")]);
        string? changed = null;
        _library.Changed += (_, folder) => changed = folder;

        Assert.Equal("Slajd 1 z 3…", _library.Load(imported.Folder).Note);
        _exporter.Release.SetResult();
        await WaitFor(() => changed is not null);

        var state = _library.Load(imported.Folder);
        Assert.Equal(imported.Folder, changed);
        Assert.Equal(3, state.Slides.Count);
        Assert.Null(state.Note);
        Assert.True(File.Exists(Path.Combine(_temp, "prezentacje", imported.Folder, "Moc Słowa.pptx")));
    }

    [Fact]
    public async Task Failed_export_reports_error_and_can_be_retried()
    {
        _exporter.Error = "Brak PowerPointa";
        var imported = await _library.ImportAsync([Source("a.pps")]);
        var changes = 0;
        _library.Changed += (_, _) => changes++;

        _library.Load(imported.Folder);
        _exporter.Release.SetResult();
        await WaitFor(() => changes == 1);
        Assert.True(_library.Load(imported.Folder).IsFailed);
        Assert.Equal("Brak PowerPointa", _library.Peek(imported.Folder).Note);

        _exporter.Error = null;
        _library.Retry(imported.Folder);
        _library.Load(imported.Folder);
        await WaitFor(() => changes == 3);
        Assert.Equal(3, _library.Load(imported.Folder).Slides.Count);
    }

    [Fact]
    public async Task Old_export_of_animated_presentation_is_redone_and_kept_when_that_fails()
    {
        var source = Path.Combine(_temp, "Animacje.pptx");
        File.WriteAllText(source, "");
        _exporter.Outdated = true;
        var imported = await _library.ImportAsync([source]);
        var slides = Path.Combine(_temp, "prezentacje", imported.Folder, "slajdy");
        Directory.CreateDirectory(slides);
        File.WriteAllText(Path.Combine(slides, "slide001.png"), "");
        File.WriteAllText(Path.Combine(slides, ".complete"), ""); // eksport sprzed kroków animacji
        _exporter.Error = "Brak PowerPointa";
        var changed = false;
        _library.Changed += (_, _) => changed = true;

        Assert.Single(_library.Load(imported.Folder).Slides); // stare slajdy w trakcie nowego eksportu …
        _exporter.Release.SetResult();
        await WaitFor(() => changed);

        var state = _library.Load(imported.Folder);            // … i po jego błędzie
        Assert.Single(state.Slides);
        Assert.False(state.IsFailed);
    }

    [Fact]
    public async Task Old_export_without_animations_stays_and_is_checked_once()
    {
        var source = Path.Combine(_temp, "Zwykla.pptx");
        File.WriteAllText(source, "");
        var imported = await _library.ImportAsync([source]);
        var slides = Path.Combine(_temp, "prezentacje", imported.Folder, "slajdy");
        Directory.CreateDirectory(slides);
        File.WriteAllText(Path.Combine(slides, "slide001.png"), "");
        File.WriteAllText(Path.Combine(slides, ".complete"), "");

        Assert.Single(_library.Load(imported.Folder).Slides);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(slides, ".complete")));
    }

    /// <summary>Prezentacja z jednym slajdem i animowanym GIF-em na nim.</summary>
    private string DeckWithMovingPicture(string name)
    {
        var file = Path.Combine(_temp, name);
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        Write(zip, "ppt/presentation.xml", """
            <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst>
              <p:sldSz cx="9144000" cy="5143500"/>
            </p:presentation>
            """);
        Write(zip, "ppt/_rels/presentation.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
            </Relationships>
            """);
        Write(zip, "ppt/slides/slide1.xml", """
            <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:cSld><p:spTree><p:pic>
                <p:blipFill><a:blip r:embed="rId1"/></p:blipFill>
                <p:spPr><a:xfrm><a:off x="457200" y="2571750"/><a:ext cx="4572000" cy="2571750"/></a:xfrm></p:spPr>
              </p:pic></p:spTree></p:cSld>
            </p:sld>
            """);
        Write(zip, "ppt/slides/_rels/slide1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="image" Target="../media/kot.gif"/>
            </Relationships>
            """);
        var gif = new List<byte>(Encoding.ASCII.GetBytes("GIF89a"));
        for (var i = 0; i < 4; i++)
            gif.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
        gif.Add(0x3B);
        var entry = zip.CreateEntry("ppt/media/kot.gif");
        using var stream = entry.Open();
        stream.Write([.. gif]);
        return file;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public async Task Old_slides_get_their_moving_pictures_described_in_the_background()
    {
        // prezentacja dodana przed tą możliwością: slajdy są gotowe, ale nie ma jeszcze opisu ruchomych obrazków
        var imported = await _library.ImportAsync([DeckWithMovingPicture("Ogloszenia.pptx")]);
        var slides = Path.Combine(_temp, "prezentacje", imported.Folder, "slajdy");
        Directory.CreateDirectory(slides);
        File.WriteAllText(Path.Combine(slides, "slide001.png"), "");
        File.WriteAllText(Path.Combine(slides, ".complete"), "v2");
        var changed = false;
        _library.Changed += (_, _) => changed = true;

        // pierwszy odczyt nie czeka na przejrzenie prezentacji — slajdy pokazują się od razu, jeszcze nieruchome
        Assert.Empty(_library.Load(imported.Folder).AnimationsOf("slide001.png"));
        await WaitFor(() => changed);

        var picture = Assert.Single(_library.Load(imported.Folder).AnimationsOf("slide001.png"));
        Assert.True(File.Exists(picture.File));
        // opis powstał raz — kolejne odczyty planu nie przeglądają prezentacji od nowa
        var described = File.GetLastWriteTimeUtc(Path.Combine(slides, SlideAnimations.MapFile));
        _library.Load(imported.Folder);
        Assert.Equal(described, File.GetLastWriteTimeUtc(Path.Combine(slides, SlideAnimations.MapFile)));
    }

    [Fact]
    public async Task Presentation_item_shows_image_slides_and_survives_plan_save()
    {
        var imported = await _library.ImportAsync([Source("b.png"), Source("a.png")]);
        var item = new PresentationPlanItem(Guid.NewGuid(), imported.Folder, imported.Title);
        var type = new PresentationPlanItemType(_library);

        var live = type.Present(item)!;
        var store = new JsonPlanStore(Path.Combine(_temp, "plany"));
        store.Save(Plan.Create("Test", null).Insert(0, item));

        Assert.Equal(PlanItemKind.Presentation, type.Describe(item)!.Kind);
        Assert.False(live.ShowTitle);
        Assert.Equal([1, 2], live.Slides.Cast<ImageSlide>().Select(s => s.Number));
        Assert.Equal(item, Assert.Single(store.LoadAll()).Items.Single());
        Assert.Contains("\"presentation\"", File.ReadAllText(Directory.GetFiles(Path.Combine(_temp, "plany")).Single()));
    }

    [Fact]
    public async Task Copy_is_an_independent_folder_with_the_same_slides()
    {
        var imported = await _library.ImportAsync([Source("a.png"), Source("b.png")]);

        var copy = await _library.CopyAsync(imported.Folder);

        Assert.NotEqual(imported.Folder, copy);
        Assert.EndsWith(imported.Folder[8..], copy);
        Assert.Equal(2, _library.Load(copy).Slides.Count);
        Assert.Empty(_library.Load(copy).Slides.Intersect(_library.Load(imported.Folder).Slides));
    }

    [Fact]
    public async Task Failed_import_leaves_no_folder_behind()
    {
        var missing = Path.Combine(_temp, "brak.png"); // np. zablokowany albo wyjęty pendrive

        await Assert.ThrowsAnyAsync<IOException>(() => _library.ImportAsync([Source("a.png"), missing]));

        var root = Path.Combine(_temp, "prezentacje");
        Assert.True(!Directory.Exists(root) || Directory.GetDirectories(root).Length == 0);
    }

    [Fact]
    public async Task Several_images_are_all_or_nothing()
    {
        var images = await _library.ImportEachAsync([Source("a.png"), Source("b.png")]);
        Assert.Equal(["a", "b"], images.Select(i => i.Title));
        Assert.All(images, i => Assert.Single(_library.Load(i.Folder).Slides));

        var missing = Path.Combine(_temp, "brak.png");
        await Assert.ThrowsAnyAsync<IOException>(() => _library.ImportEachAsync([Source("c.png"), missing]));
        // „c” skopiowane przed błędem zostało usunięte — zostały tylko dwa udane
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(_temp, "prezentacje")).Length);
    }

    [Fact]
    public async Task Failed_copy_leaves_no_folder_behind()
    {
        var imported = await _library.ImportAsync([Source("a.png")]);
        Directory.Delete(Path.Combine(_temp, "prezentacje", imported.Folder), recursive: true); // źródło zniknęło

        await Assert.ThrowsAnyAsync<IOException>(() => _library.CopyAsync(imported.Folder));

        Assert.Empty(Directory.GetDirectories(Path.Combine(_temp, "prezentacje")));
    }

    [Fact]
    public async Task Unsupported_files_are_rejected() =>
        await Assert.ThrowsAsync<NotSupportedException>(() => _library.ImportAsync([Source("x.txt")]));

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }
}

/// <summary>Dopasowanie obrazu do rzutnika: macierz perspektywy i ograniczenia narożników.</summary>
public class KeystoneTests
{
    [Fact]
    public void Rectangle_corners_land_on_the_quad()
    {
        IReadOnlyList<CornerPoint> quad = [new(100, 50), new(1800, 120), new(1700, 1000), new(200, 1060)];
        var m = Keystone.RectToQuad(1920, 1080, quad);

        var mapped = new[] { (0d, 0d), (1920d, 0d), (1920d, 1080d), (0d, 1080d) }
            .Select(p => Keystone.Apply(m, p.Item1, p.Item2)).ToList();

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(quad[i].X, mapped[i].X, 6);
            Assert.Equal(quad[i].Y, mapped[i].Y, 6);
        }
    }

    [Fact]
    public void Invalid_settings_mean_full_screen()
    {
        Assert.Same(Keystone.FullScreen, Keystone.FromValues(null));
        Assert.Same(Keystone.FullScreen, Keystone.FromValues([0, 0, 1, 0]));
        // przecięty czworokąt (zamienione dolne narożniki)
        Assert.Same(Keystone.FullScreen, Keystone.FromValues([0, 0, 1, 0, 0, 1, 1, 1]));
        Assert.Equal(0.1, Keystone.FromValues([0.1, 0, 1, 0, 1, 1, 0, 1])[0].X);
    }

    [Fact]
    public void Scaling_stays_on_screen()
    {
        var smaller = Keystone.Scale(Keystone.FullScreen, 0.9)!;
        Assert.Equal(0.05, smaller[0].X, 9);
        Assert.Null(Keystone.Scale(Keystone.FullScreen, 1.1));
        Assert.Equal(0, Keystone.Move(smaller, -1, 0).Min(c => c.X), 9);
    }

    [Fact]
    public void Width_and_height_scale_separately()
    {
        var narrower = Keystone.Scale(Keystone.FullScreen, 0.8, 1)!;
        Assert.Equal(0.1, narrower[0].X, 9);
        Assert.Equal(0, narrower[0].Y, 9);
    }

    [Fact]
    public void Settings_round_trip() =>
        Assert.Equal(Keystone.FullScreen, Keystone.FromValues(Keystone.ToValues(Keystone.FullScreen)));

    [Fact]
    public void Item_types_forward_content_changes()
    {
        var library = new Songs.SongLibrary(new Songs.DirectorySongSource(RepositoryPaths.Songs), new Songs.MarkdownSongParser());
        var types = new PlanItemTypes([new SongPlanItemType(library, new SectionSlideBuilder())]);
        var changes = 0;
        types.Changed += (_, _) => changes++;

        library.Reload();

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Plan_item_json_keeps_older_plans_readable()
    {
        var json = """{"id":"00000000-0000-0000-0000-000000000001","name":"P","date":null,"kind":"Event","items":[{"type":"song","id":"00000000-0000-0000-0000-000000000002","songId":"047-x","arrangement":null}],"updatedAt":"2026-01-01T00:00:00+00:00"}""";
        var plan = JsonSerializer.Deserialize<Plan>(json, JsonPlanStore.JsonOptions)!;
        Assert.IsType<SongPlanItem>(Assert.Single(plan.Items));
    }
}
