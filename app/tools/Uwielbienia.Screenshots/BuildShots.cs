using Avalonia.Controls;
using Uwielbienia.App.Transitions;
using Avalonia.Headless;
using Avalonia.Threading;
using Uwielbienia.App.Services;
using Uwielbienia.App.ViewModels;
using Uwielbienia.App.Views;
using Uwielbienia.Core.Presentation;
using Uwielbienia.Core.Slideshows;
using Uwielbienia.Core.Slideshows.Builds;
using SkiaSharp;

/// <summary>
/// Próba ruchu elementów z prawdziwym plikiem (<c>--build &lt;prezentacja&gt;</c>): eksport przez DeckCraft do
/// <c>&lt;katalog&gt;/budowa</c> i klatki ruchu każdego kroku (początek, ćwierć, połowa, trzy ćwierci, koniec) jako PNG.
/// </summary>
internal static class BuildShots
{
    public static void Run(string presentation, string output)
    {
        var dir = Path.Combine(output, "budowa");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var exporter = new DeckCraftBuildExporter();
        if (!exporter.IsAvailable)
        {
            Console.WriteLine("Brak DeckCraft");
            return;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var counts = exporter.ExportAsync(presentation, dir, 1920, new SyncProgress()).GetAwaiter().GetResult();
        Console.WriteLine(counts is null ? "Prezentacja się nie nadaje (eksport zwykły)" : $"Obrazy slajdów: {string.Join(", ", counts)} ({watch.ElapsedMilliseconds} ms)");
        if (counts is null)
            return;
        var slides = Directory.GetFiles(dir, "slide*.png").Order().ToList();
        if (slides.Count >= 2)
            Transitions(slides[0], slides[^1], output);
        foreach (var build in SlideBuilds.Load(dir).Values.Select(f => f.Build).DistinctBy(b => b.Key))
        {
            using var images = SlideBuildImages.Load(build) ?? throw new InvalidOperationException("warstwy");
            var name = Path.GetFileNameWithoutExtension(build.Images[0]);
            Console.WriteLine($"{name}: kroków {build.Timeline.Steps} (automatyczny: {build.AutoSteps}), warstw {build.Layers.Count}, obrazów {build.Images.Count}");
            for (var image = 0; image < build.Images.Count; image++)
            {
                if (build.PlayStep(image) is not { } step)
                    continue;
                var end = build.End(step);
                for (var f = 0; f <= 4; f++)
                    SlideBuildRenderer.Save(build, images, new BuildMoment(step, end * f / 4), 960,
                        Path.Combine(output, $"ruch-{name}-stan{image}-{f * 25:D3}.png"));
            }
            // strona rzutnika (SlideView → AnimatedSlideImage) w stanie po ostatnim kroku i w trakcie ruchu do niego
            var last = build.Images.Count - 1;
            var slide = new ImageSlide(Path.Combine(dir, build.Images[last]), last + 1, null, new SlideBuildFrame(build, last));
            ShowPage(new ProjectedImage(slide, BuildMotion.Still), Path.Combine(output, $"widok-{name}-stan{last}.png"), 10);
            ShowPage(new ProjectedImage(slide, new BuildMotion(Play: true)), Path.Combine(output, $"widok-{name}-stan{last}-ruch.png"), 3);
        }
    }

    /// <summary>Klatki kilku przejść z pliku (geometria DeckCraft) między dwoma obrazami — do oka.</summary>
    public static void Transitions(string fromFile, string toFile, string output)
    {
        using var from = SKImage.FromEncodedData(fromFile);
        using var to = SKImage.FromEncodedData(toFile);
        foreach (var (kind, option) in new[] { ("fade", ""), ("push", "l"), ("wipe", "r"), ("split", "horzOut"), ("cube", "l"), ("vortex", "l"), ("curtains", ""), ("ripple", "center") })
        {
            using var surface = SKSurface.Create(new SKImageInfo(960 * 3, 540, SKColorType.Rgba8888, SKAlphaType.Premul));
            for (var i = 0; i < 3; i++)
            {
                var t = 0.25 + i * 0.25;
                QuadTransitionView.Draw(surface.Canvas, SKRect.Create(i * 960, 0, 960, 540), from, to, SlideTransitionFrames.Frame(kind, option, t, 16.0 / 9), kind, option);
            }
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(output, $"przejscie-pliku-{kind}.png"));
            data.SaveTo(stream);
        }
    }

    private static void ShowPage(ProjectedPage page, string file, int ticks)
    {
        var window = new Window { Width = 960, Height = 540, Content = new SlideView { Page = page } };
        window.Show();
        for (var i = 0; i < ticks; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(60);
        }
        window.CaptureRenderedFrame()?.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
    }

    private sealed class SyncProgress : IProgress<string>
    {
        public void Report(string value) => Console.WriteLine(value);
    }
}
