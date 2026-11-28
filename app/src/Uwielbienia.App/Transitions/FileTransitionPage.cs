using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.VisualTree;
using SkiaSharp;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.App.Transitions;

/// <summary>
/// Przejście zapisane w prezentacji (PowerPoint: „Przejścia”) — geometria z kodu przeniesionego z DeckCraft
/// (<see cref="SlideTransitionFrames"/>: warstwy z teksturą starego / nowego slajdu, rysowane siatką trójkątów —
/// <see cref="TransitionMesh"/>). Morph jest odtwarzany jako przenikanie. Zrzuty stron jak w
/// <see cref="ShaderPageTransition"/>. Wstecz: przejście opuszczanego slajdu od końca (<see cref="SlideTransitionFrames.ReverseFrame"/>).
/// Przejścia z osobnymi warstwami tła i zawartości poruszają całymi slajdami według geometrii warstw zawartości.
/// Płynne Odkrywanie używa też całych slajdów jako przenikających się teł.
/// </summary>
public sealed class FileTransitionPage(FileTransition transition, bool reverse) : IPageTransition
{
    public FileTransition Transition { get; } = transition;

    public bool Reverse { get; } = reverse;

    public TimeSpan Duration { get; } = TimeSpan.FromSeconds(transition.DurationSeconds);

    /// <summary>Zwykłe cięcie — nowy slajd od razu; cięcie „przez czerń” ma czerń w połowie, więc gra jak każde przejście.</summary>
    private bool IsPlainCut => Transition is { Kind: "cut", Option: not "throughBlack" };

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var id = TransitionLog.NextId();
        TransitionPageOwners.Claim(from, id);
        TransitionPageOwners.Claim(to, id);
        if (cancellationToken.IsCancellationRequested)
            return;
        var name = $"#{id} przejście z pliku {Transition.Kind}{(Transition.Option.Length > 0 ? "/" + Transition.Option : "")}{(Reverse ? " wstecz" : "")}";
        if (Duration <= TimeSpan.Zero || IsPlainCut)
        {
            TransitionLog.Write($"{name}: od razu");
            await new CutTransition().Start(from, to, forward, cancellationToken);
            return;
        }
        if (from is null || to is null || to.GetVisualParent() is not Panel host || !TransitionCatalog.IsKnown(Transition.Kind))
        {
            TransitionLog.Write($"{name}: PRZENIKANIE — {(TransitionCatalog.IsKnown(Transition.Kind) ? "brak strony" : "nieznane przejście")}");
            await new DirectCrossFade(Duration).Start(from, to, forward, cancellationToken);
            return;
        }

        to.IsVisible = true;
        to.Opacity = 0;
        from.Opacity = 1;
        await PageReadiness.WaitAsync(to, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            if (TransitionPageOwners.Owns(to, id))
                to.Opacity = 1;
            return;
        }
        to.Opacity = 1;
        var fromImage = ShaderPageTransition.Capture(from);
        var toImage = ShaderPageTransition.Capture(to);
        if (fromImage is null || toImage is null)
        {
            fromImage?.Dispose();
            toImage?.Dispose();
            to.Opacity = 0;
            TransitionLog.Write($"{name}: PRZENIKANIE — nie udał się zrzut strony");
            await new DirectCrossFade(Duration).Start(from, to, forward, cancellationToken);
            return;
        }
        TransitionLog.Write($"{name}: start, {Duration.TotalSeconds:0.##} s");
        var view = new QuadTransitionView(fromImage, toImage, Transition, Reverse)
        {
            Width = to.Bounds.Width,
            Height = to.Bounds.Height,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        host.Children.Add(view);
        from.Opacity = 0;
        to.Opacity = 0;
        try
        {
            await Animate(view, cancellationToken);
        }
        finally
        {
            host.Children.Remove(view);
            if (TransitionPageOwners.Owns(to, id))
                to.Opacity = 1;
            if (TransitionPageOwners.Owns(from, id))
                from.Opacity = 1;
            if (!cancellationToken.IsCancellationRequested)
                from.IsVisible = false;
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                fromImage.Dispose();
                toImage.Dispose();
            }, TaskScheduler.Default);
        }
    }

    private Task Animate(QuadTransitionView view, CancellationToken cancellationToken)
    {
        if (TopLevel.GetTopLevel(view) is not { } top)
            return Task.CompletedTask;
        var done = new TaskCompletionSource();
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                done.TrySetResult();
                return;
            }
            start ??= now;
            view.Progress = (now - start.Value) / Duration;
            if (view.Progress >= 1)
                done.TrySetResult();
            else
                top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
        return done.Task;
    }
}

/// <summary>Klatka przejścia z pliku: warstwy DeckCraft narysowane na czerni (siatki trójkątów z teksturą zrzutów).</summary>
public sealed class QuadTransitionView : Control
{
    private readonly SKImage _from;
    private readonly SKImage _to;
    private readonly FileTransition _transition;
    private readonly bool _reverse;
    private double _progress;

    public QuadTransitionView(SKImage from, SKImage to, FileTransition transition, bool reverse)
    {
        _from = from;
        _to = to;
        _transition = transition;
        _reverse = reverse;
        IsHitTestVisible = false;
    }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0, 1);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
            return;
        var aspect = size.Width / size.Height;
        var layers = _reverse
            ? SlideTransitionFrames.ReverseFrame(_transition.Kind, _transition.Option, _progress, aspect)
            : SlideTransitionFrames.Frame(_transition.Kind, _transition.Option, _progress, aspect);
        context.Custom(new Operation(new Rect(size), _from, _to, layers, _transition));
    }

    /// <summary>Rysuje warstwy przejścia na płótnie (wspólne z próbami w narzędziu zrzutów).</summary>
    public static void Draw(SKCanvas canvas, SKRect rect, SKImage from, SKImage to, IReadOnlyList<TransitionLayer> layers, string kind, string option)
    {
        var moveWholeSlides = SlideTransitionFrames.UsesSlideParts(kind, option);
        var drawBackgrounds = kind == "reveal";
        var paints = new Dictionary<TransitionSource, SKPaint>();
        canvas.Save();
        try
        {
            canvas.ClipRect(rect);
            using (var black = new SKPaint { Color = SKColors.Black })
                canvas.DrawRect(rect, black);
            foreach (var layer in layers)
            {
                if (SourceForDrawing(layer.Source, moveWholeSlides, drawBackgrounds) is not { } source)
                    continue;
                var mesh = TransitionMesh.LayerMesh(layer, out var triangles);
                if (mesh.Count < 3 || triangles.Count < 3)
                    continue;
                var image = source switch { TransitionSource.Old => from, TransitionSource.New => to, _ => null };
                var positions = mesh.Select(v => new SKPoint(rect.Left + (float)(v.Position.X * rect.Width), rect.Top + (float)(v.Position.Y * rect.Height))).ToArray();
                var colors = mesh.Select(v => SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(v.Opacity, 0, 1) * 255))).ToArray();
                var indices = triangles.Select(i => (ushort)i).ToArray();
                var texture = image is null
                    ? positions
                    : mesh.Select(v => new SKPoint((float)(v.Uv.X * image.Width), (float)(v.Uv.Y * image.Height))).ToArray();
                if (!paints.TryGetValue(source, out var paint))
                {
                    paint = CreateLayerPaint(source, image);
                    paints.Add(source, paint);
                }
                canvas.DrawVertices(SKVertexMode.Triangles, positions, texture, colors, SKBlendMode.Modulate, indices, paint);
            }
        }
        finally
        {
            foreach (var paint in paints.Values)
                paint.Dispose();
            canvas.Restore();
        }
    }

    private static SKPaint CreateLayerPaint(TransitionSource source, SKImage? image)
    {
        using var shader = image is null
            ? SKShader.CreateColor(source == TransitionSource.White ? SKColors.White : SKColors.Black)
            : image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
        return new SKPaint { IsAntialias = true, Shader = shader };
    }

    private static TransitionSource? SourceForDrawing(TransitionSource source, bool moveWholeSlides, bool drawBackgrounds) =>
        source switch
        {
            TransitionSource.OldBack or TransitionSource.NewBack when moveWholeSlides && !drawBackgrounds => null,
            TransitionSource.OldContent when moveWholeSlides => TransitionSource.Old,
            TransitionSource.NewContent when moveWholeSlides => TransitionSource.New,
            _ => source.Whole(),
        };

    private sealed class Operation(Rect bounds, SKImage from, SKImage to, IReadOnlyList<TransitionLayer> layers, FileTransition transition) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;
            using var lease = feature.Lease();
            Draw(lease.SkCanvas, SKRect.Create((float)bounds.Width, (float)bounds.Height), from, to, layers, transition.Kind, transition.Option);
        }
    }
}
