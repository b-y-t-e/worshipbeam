using System.Numerics;
using SkiaSharp;
using Uwielbienia.Core.Slideshows;

namespace Uwielbienia.App.Services;

/// <summary>Slajd z ruchem elementów na ekranie: który stan, czy odegrać ruch prowadzący do niego i po jakim czasie.</summary>
public sealed record SlideBuildPlayback(SlideBuildFrame Frame, bool Play, bool WaitForTransition);

/// <summary>Obrazy budowy slajdu w pamięci: tło i każda warstwa (z wariantami koloru), według pełnej ścieżki pliku.</summary>
/// <remarks>
/// Obrazy z pamięci podręcznej rzutnika (<see cref="LoadAsync"/>) nie są zwalniane jawnie — może je jeszcze rysować wątek
/// renderowania; zwalnia je ten, kto wczytał je sam (<see cref="Load"/>, np. eksport).
/// </remarks>
public sealed class SlideBuildImages(SKImage background, IReadOnlyDictionary<string, SKImage> layers) : IDisposable
{
    public SKImage Background { get; } = background;

    public SKImage? Layer(string file) => layers.GetValueOrDefault(file);

    public void Dispose()
    {
        Background.Dispose();
        foreach (var layer in layers.Values)
            layer.Dispose();
    }

    /// <summary>Wczytuje obrazy budowy; <c>null</c>, gdy któregoś nie da się odczytać.</summary>
    public static SlideBuildImages? Load(SlideBuild build)
    {
        if (Decode(build.Background) is not { } background)
            return null;
        var layers = new Dictionary<string, SKImage>();
        foreach (var file in build.Layers.SelectMany(l => l.Tints.Select(t => t.File).Prepend(l.File)))
        {
            if (Decode(file) is not { } image)
                return null;
            layers[file] = image;
        }
        return new SlideBuildImages(background, layers);
    }

    private static SKImage? Decode(string file)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(file);
            return bitmap is null ? null : SKImage.FromBitmap(bitmap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Obrazy budów: rzutnik i kolumna „Na ekranie” rysują ten sam slajd, a „Dalej” wraca do tych samych warstw — dekodujemy raz.
    /// </summary>
    private static readonly TaskCache<SlideBuildImages> Cache = new(capacity: 3);

    public static Task<SlideBuildImages?> LoadAsync(SlideBuild build) =>
        Cache.Get(build.Key, () => Task.Run(() => Load(build)));
}

/// <summary>
/// Rysuje budowę slajdu w danej chwili (<see cref="SlideBuild.Poses"/>) na płótnie Skia — ten sam kod dla obrazów stanów
/// przy eksporcie i dla rzutnika, więc slajd w ruchu kończy się dokładnie na swoim obrazie.
/// </summary>
public static class SlideBuildRenderer
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>Rysuje slajd w prostokącie <paramref name="slide"/> (w pikselach płótna).</summary>
    public static void Draw(SKCanvas canvas, SKRect slide, SlideBuild build, SlideBuildImages images, IReadOnlyList<LayerPose> poses)
    {
        canvas.Save();
        // to, co wylatuje poza slajd, jest w prezentacji niewidoczne
        canvas.ClipRect(slide, antialias: true);
        canvas.DrawImage(images.Background, slide, Sampling);
        var scale = slide.Width / (float)build.SlideWidth;
        foreach (var pose in poses)
        {
            if (images.Layer(pose.Layer.File) is not { } layer)
                continue;
            canvas.Save();
            canvas.Translate(slide.Left, slide.Top);
            canvas.Scale(scale);
            canvas.Concat(ToSkia(pose.Transform));
            if (pose.Clip is { Count: > 2 } clip)
            {
                using var path = new SKPath();
                path.AddPoly(clip.Select(p => new SKPoint(p.X, p.Y)).ToArray(), close: true);
                canvas.ClipPath(path, antialias: true);
            }
            var rect = SKRect.Create((float)pose.Layer.X, (float)pose.Layer.Y, (float)pose.Layer.Width, (float)pose.Layer.Height);
            using (var paint = Paint(pose.Opacity))
                canvas.DrawImage(layer, rect, Sampling, paint);
            if (pose.Tint is { } tint && images.Layer(tint.File) is { } tinted)
            {
                // wypełnienie w innym kolorze na wierzchu z udziałem zmiany — dla jednolitego koloru to dokładnie kolor pośredni
                using var paint = Paint(pose.Opacity * pose.TintAmount);
                canvas.DrawImage(tinted, rect, Sampling, paint);
            }
            canvas.Restore();
        }
        canvas.Restore();
    }

    /// <summary>Obraz slajdu w chwili <paramref name="moment"/> o szerokości <paramref name="width"/> pikseli (PNG).</summary>
    public static void Save(SlideBuild build, SlideBuildImages images, BuildMoment moment, int width, string file)
    {
        var height = (int)Math.Round(width * build.SlideHeight / build.SlideWidth);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("SKSurface");
        surface.Canvas.Clear(SKColors.Transparent);
        Draw(surface.Canvas, SKRect.Create(width, height), build, images, build.Poses(moment));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(file);
        data.SaveTo(stream);
    }

    private static SKPaint? Paint(double opacity) =>
        opacity >= 0.999 ? null : new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255)) };

    /// <summary>Macierz wierszowa (System.Numerics, jak Avalonia) jako macierz Skia.</summary>
    public static SKMatrix ToSkia(Matrix3x2 m) => new(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1);
}
