using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Uwielbienia.App.Services;
using Uwielbienia.Core.Slideshows;

namespace Uwielbienia.App.Views;

/// <summary>
/// Slajd prezentacji: obraz na cały ekran, a na nim ruchome obrazki (animowane GIF-y z prezentacji — eksport zamienia
/// slajd w nieruchomy PNG, więc GIF zastygłby na pierwszej klatce). Kontrolka sama liczy, gdzie leży obraz slajdu
/// (mieści go w swoich granicach bez zniekształcenia) i rysuje na nim każdy GIF w jego miejscu ze slajdu.
/// </summary>
public sealed class AnimatedSlideImage : Control
{
    /// <summary>Nie trzymamy w pamięci bardzo długich animacji — dłuższy GIF zostaje nieruchomy.</summary>
    private const int MaxFrames = 300;

    /// <summary>
    /// Najwięcej pamięci na klatki jednego GIF-a. Komputer przy rzutniku ma jej niewiele, a obok trzymane są obrazy
    /// slajdów — większy obrazek zostaje nieruchomy zamiast zabrać pamięć w trakcie nabożeństwa.
    /// </summary>
    private const long MaxFrameBytes = 48L * 1024 * 1024;

    /// <summary>
    /// Najszersza klatka w pamięci: rzutnik rysuje slajd na płótnie 1920 px, więc nic więcej nie wnosi, a GIF na cały
    /// slajd mieści się wtedy w <see cref="MaxFrameBytes"/> (jak zmniejszanie obrazów slajdów w <see cref="SlideImages"/>).
    /// </summary>
    private const int MaxFrameWidth = 1280;

    /// <summary>
    /// Klatki wczytanych GIF-ów: powrót na ten sam slajd nie dekoduje ich jeszcze raz, a najdawniej używane wypadają
    /// (pamięć zwalnia je razem z nimi, jak w <see cref="SlideImages"/>).
    /// </summary>
    private static readonly TaskCache<MovingFrames> Cache = new(capacity: 4);

    /// <summary>Klatka bez podanego czasu (albo z zerowym) trwa tyle, co w przeglądarkach.</summary>
    private static readonly TimeSpan DefaultFrameTime = TimeSpan.FromMilliseconds(100);

    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<AnimatedSlideImage, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<IReadOnlyList<SlideAnimation>?> AnimationsProperty =
        AvaloniaProperty.Register<AnimatedSlideImage, IReadOnlyList<SlideAnimation>?>(nameof(Animations));

    static AnimatedSlideImage()
    {
        AffectsRender<AnimatedSlideImage>(SourceProperty);
        AnimationsProperty.Changed.AddClassHandler<AnimatedSlideImage>((control, _) => control.Reload());
    }

    /// <summary>Obraz slajdu.</summary>
    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Ruchome obrazki tego slajdu; pusta lista = zwykły, nieruchomy slajd.</summary>
    public IReadOnlyList<SlideAnimation>? Animations
    {
        get => GetValue(AnimationsProperty);
        set => SetValue(AnimationsProperty, value);
    }

    /// <summary>Wczytany GIF: klatki i czas każdej z nich.</summary>
    private sealed record MovingFrames(IReadOnlyList<Bitmap> Frames, IReadOnlyList<TimeSpan> Times)
    {
        public TimeSpan Length => Times.Aggregate(TimeSpan.Zero, (sum, time) => sum + time);

        /// <summary>Klatka pokazywana po <paramref name="elapsed"/> od początku animacji (w kółko).</summary>
        public Bitmap FrameAt(TimeSpan elapsed)
        {
            var length = Length;
            var position = length > TimeSpan.Zero ? TimeSpan.FromTicks(elapsed.Ticks % length.Ticks) : TimeSpan.Zero;
            for (var i = 0; i < Frames.Count; i++)
            {
                if (position < Times[i])
                    return Frames[i];
                position -= Times[i];
            }
            return Frames[^1];
        }
    }

    /// <summary>Ruchomy obrazek gotowy do rysowania: gdzie stoi i jego klatki.</summary>
    private sealed record MovingPicture(SlideAnimation Where, MovingFrames Picture);

    private IReadOnlyList<MovingPicture> _pictures = [];
    private CancellationTokenSource? _loading;
    private TimeSpan? _started;
    private TimeSpan _elapsed;
    private bool _drawing;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loading?.Cancel();
        _drawing = false;
        _started = null;
    }

    /// <summary>
    /// Klatki dekodują się w tle: slajd wchodzi na ekran razem z przejściem, a dekodowanie na wątku interfejsu
    /// zacinałoby je. Do czasu wczytania slajd jest nieruchomy (z wypaloną pierwszą klatką GIF-a).
    /// </summary>
    private void Reload()
    {
        _loading?.Cancel();
        _pictures = [];
        _started = null;
        _elapsed = TimeSpan.Zero;
        InvalidateVisual();
        var wanted = Animations ?? [];
        if (wanted.Count == 0 || TopLevel.GetTopLevel(this) is null)
            return;
        var loading = new CancellationTokenSource();
        _loading = loading;
        _ = ShowWhenLoadedAsync(wanted, loading.Token);
    }

    private async Task ShowWhenLoadedAsync(IReadOnlyList<SlideAnimation> wanted, CancellationToken token)
    {
        // zadanie trafia do pamięci podręcznej wspólnej dla rzutnika i podglądu — anulowanie jednej kontrolki nie może
        // go przerwać (drugi widok dostałby anulowany wynik); czeka na nie każdy, a token pomija tylko spóźniony wynik
        var loaded = await Task.WhenAll(wanted.Select(a => Cache.Get(KeyOf(a.File), () => Task.Run(() => Load(a)))));
        if (token.IsCancellationRequested)
            return;
        _pictures = wanted.Zip(loaded)
            .Where(pair => pair.Second is not null)
            .Select(pair => new MovingPicture(pair.First, pair.Second!))
            .ToList();
        InvalidateVisual();
        Animate();
    }

    /// <summary>Rysuje kolejne klatki, póki slajd z ruchomym obrazkiem jest na ekranie.</summary>
    private void Animate()
    {
        if (_drawing || _pictures.Count == 0 || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _drawing = true;
        void Frame(TimeSpan now)
        {
            if (!_drawing || _pictures.Count == 0 || TopLevel.GetTopLevel(this) is not { } current)
            {
                _drawing = false;
                return;
            }
            _started ??= now;
            _elapsed = now - _started.Value;
            InvalidateVisual();
            current.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { } picture)
            return;
        var slide = Fitted(picture.Size, Bounds.Size);
        context.DrawImage(picture, slide);
        if (_pictures.Count == 0)
            return;
        // Obrazek wystający za brzeg slajdu jest w prezentacji przycięty do niego — poza slajdem jest tło ekranu.
        using var onSlide = context.PushClip(slide);
        foreach (var moving in _pictures)
            context.DrawImage(moving.Picture.FrameAt(_elapsed), Place(moving.Where, slide));
    }

    /// <summary>Obraz w granicach kontrolki, bez zniekształcenia i wyśrodkowany (jak <c>Stretch="Uniform"</c>).</summary>
    private static Rect Fitted(Size image, Size available)
    {
        if (image.Width <= 0 || image.Height <= 0 || available.Width <= 0 || available.Height <= 0)
            return new Rect(available);
        var scale = Math.Min(available.Width / image.Width, available.Height / image.Height);
        var size = new Size(image.Width * scale, image.Height * scale);
        return new Rect(new Point((available.Width - size.Width) / 2, (available.Height - size.Height) / 2), size);
    }

    /// <summary>Miejsce ruchomego obrazka na obrazie slajdu (jego położenie podane jest w ułamkach slajdu).</summary>
    private static Rect Place(SlideAnimation picture, Rect slide) => new(
        slide.X + picture.X * slide.Width,
        slide.Y + picture.Y * slide.Height,
        picture.Width * slide.Width,
        picture.Height * slide.Height);

    /// <summary>
    /// Klucz w pamięci podręcznej: ponowny eksport zapisuje nowe ruchome obrazki pod tymi samymi nazwami, więc do
    /// ścieżki dokładamy czas zapisu (jak przy obrazach slajdów).
    /// </summary>
    private static string KeyOf(string file) =>
        $"{file}|{(File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0)}";

    /// <summary>
    /// Klatki GIF-a (zmniejszone do <see cref="MaxFrameWidth"/>); <c>null</c> = pliku nie da się odczytać, jest zbyt
    /// długi albo jego klatki czyszczą to, co narysowały poprzednie — wtedy slajd zostaje nieruchomy. Klatki rysują się
    /// na gotowym slajdzie, w którym eksport wypalił pierwszą z nich, więc wyczyszczone miejsca odsłoniłyby jej ślad.
    /// </summary>
    private static MovingFrames? Load(SlideAnimation animation)
    {
        try
        {
            using var codec = SKCodec.Create(animation.File);
            if (codec is null || codec.FrameCount < 2)
                return null;
            // obcięta animacja zapętlałaby się w połowie, a czyszczące klatki odsłoniłyby ślad wypalony w slajdzie
            if (codec.FrameCount > MaxFrames || ClearsEarlierFrames(codec))
                return null;
            var shown = Shrunk(codec.Info, MaxFrameWidth);
            if ((long)shown.Width * shown.Height * 4 * codec.FrameCount > MaxFrameBytes)
                return null;
            var frames = new List<Bitmap>();
            var times = new List<TimeSpan>();
            using var canvas = new SKBitmap(shown);
            for (var i = 0; i < codec.FrameCount; i++)
            {
                // Klatki GIF-a bywają różnicowe. W buforze leży poprzednia klatka, więc mówimy o tym kodekowi tylko
                // wtedy, gdy tej właśnie potrzebuje; inaczej niech złoży klatkę sam — różnica dołożona do złego tła
                // zostawiłaby na obrazku smugi.
                var options = codec.FrameInfo[i].RequiredFrame == i - 1 ? new SKCodecOptions(i, i - 1) : new SKCodecOptions(i);
                if (codec.GetPixels(canvas.Info, canvas.GetPixels(), options) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    break;
                frames.Add(ToBitmap(canvas));
                var milliseconds = codec.FrameInfo[i].Duration;
                times.Add(milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : DefaultFrameTime);
            }
            return frames.Count > 1 ? new MovingFrames(frames, times) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Czy któraś klatka czyści to, co narysowały poprzednie („przywróć tło” albo „przywróć poprzednią”). Takie klatki
    /// mają przezroczyste miejsca tam, gdzie wcześniej coś było, więc na gotowym slajdzie prześwitywałaby przez nie
    /// pierwsza klatka wypalona w obrazie slajdu. Klatki tylko dokładane (zwykły przypadek) rysują się poprawnie, bo
    /// przezroczyste zostają miejsca, których GIF nigdy nie malował — tam na slajdzie jest jego tło.
    /// </summary>
    private static bool ClearsEarlierFrames(SKCodec codec) =>
        codec.FrameInfo.Any(frame => frame.DisposalMethod != SKCodecAnimationDisposalMethod.Keep);

    /// <summary>Rozmiar klatek w pamięci: nie szerzej niż <paramref name="width"/>, w proporcjach pliku.</summary>
    private static SKImageInfo Shrunk(SKImageInfo info, int width)
    {
        var scale = info.Width > width ? width / (double)info.Width : 1;
        return new SKImageInfo(
            Math.Max(1, (int)Math.Round(info.Width * scale)),
            Math.Max(1, (int)Math.Round(info.Height * scale)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
    }

    private static Bitmap ToBitmap(SKBitmap frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = bitmap.Lock();
        var pixels = frame.Bytes;
        for (var row = 0; row < frame.Height; row++)
            Marshal.Copy(pixels, row * frame.RowBytes, locked.Address + row * locked.RowBytes, Math.Min(frame.RowBytes, locked.RowBytes));
        return bitmap;
    }
}
