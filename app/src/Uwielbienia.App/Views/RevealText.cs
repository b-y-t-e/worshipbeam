using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Uwielbienia.Core.Plans;

namespace Uwielbienia.App.Views;

/// <summary>
/// Tekst napisu kiosku, który może pojawiać się słowo po słowie albo wiersz po wierszu (<see cref="KioskTextReveal"/>):
/// wszystkie słowa w czasie <see cref="DurationProperty"/>, więc napis składa się szybciej, niż jest widoczny.
/// Każde słowo to osobny <see cref="TextBlock"/> (Avalonia nie animuje pojedynczych fragmentów jednego bloku tekstu),
/// dlatego kontrolka sama zawija wiersze i wyśrodkowuje je — <see cref="WrapPanel"/> dosunąłby je do lewej.
/// </summary>
public sealed class RevealText : Panel
{
    /// <summary>Jak długo pojawia się jedno słowo.</summary>
    private static readonly TimeSpan WordFade = TimeSpan.FromSeconds(0.45);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RevealText, string?>(nameof(Text));

    public static readonly StyledProperty<KioskTextReveal> ModeProperty =
        AvaloniaProperty.Register<RevealText, KioskTextReveal>(nameof(Mode));

    /// <summary>W tym czasie pojawiają się wszystkie słowa; zero = cały tekst od razu.</summary>
    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<RevealText, TimeSpan>(nameof(Duration));

    /// <summary>Opóźnienie przed pierwszym słowem (tekst pod tytułem zaczyna się po nim).</summary>
    public static readonly StyledProperty<TimeSpan> StartDelayProperty =
        AvaloniaProperty.Register<RevealText, TimeSpan>(nameof(StartDelay));

    static RevealText()
    {
        AffectsMeasure<RevealText>(TextProperty);
        TextProperty.Changed.AddClassHandler<RevealText>((control, _) => control.Rebuild());
        ModeProperty.Changed.AddClassHandler<RevealText>((control, _) => control.Rebuild());
        DurationProperty.Changed.AddClassHandler<RevealText>((control, _) => control.Rebuild());
        StartDelayProperty.Changed.AddClassHandler<RevealText>((control, _) => control.Rebuild());
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public KioskTextReveal Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public TimeSpan Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public TimeSpan StartDelay
    {
        get => GetValue(StartDelayProperty);
        set => SetValue(StartDelayProperty, value);
    }

    /// <summary>Słowo z informacją, czy zaczyna nowy wiersz (twardy koniec linii w tekście).</summary>
    private readonly List<bool> _breaks = [];
    private CancellationTokenSource? _reveal;

    /// <summary>Opóźnienia słów czekające na pokazanie napisu; <c>null</c> = nic się nie składa.</summary>
    private IReadOnlyList<TimeSpan>? _waiting;

    private void Rebuild()
    {
        _reveal?.Cancel();
        _reveal = null;
        _waiting = null;
        Children.Clear();
        _breaks.Clear();
        if (string.IsNullOrEmpty(Text))
            return;

        var lines = Text.ReplaceLineEndings("\n").Split('\n');
        var delays = new List<TimeSpan>();
        var reveal = Mode == KioskTextReveal.Instant ? TimeSpan.Zero : Duration;
        var steps = Steps(lines, Mode);
        var step = steps > 0 && reveal > TimeSpan.Zero ? reveal / steps : TimeSpan.Zero;
        var index = 0;
        for (var line = 0; line < lines.Length; line++)
        {
            var words = lines[line].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Pusty wiersz: zastępcza spacja trzyma odstęp, ale nie jest słowem — nie liczy się do czasu składania
            // (tak samo jak w Steps), inaczej tekst składałby się dłużej, niż ustawił operator.
            var blank = words.Length == 0;
            if (blank)
                words = [" "];
            for (var word = 0; word < words.Length; word++)
            {
                // Wyraz dłuższy niż całe pole (np. adres strony) łamie się w środku — inaczej wyszedłby poza slajd.
                Children.Add(new TextBlock { Text = words[word], TextWrapping = TextWrapping.Wrap, Opacity = step > TimeSpan.Zero ? 0 : 1 });
                _breaks.Add(line > 0 && word == 0);
                delays.Add(StartDelay + step * (Mode == KioskTextReveal.Line ? line : index));
                if (!blank)
                    index++;
            }
        }
        if (step <= TimeSpan.Zero)
            return;
        _waiting = delays;
        StartWhenVisible();
    }

    private static int Steps(string[] lines, KioskTextReveal mode) =>
        mode == KioskTextReveal.Line ? lines.Length : lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartWhenVisible();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _reveal?.Cancel();
        _reveal = null;
    }

    /// <summary>
    /// Słowa zaczynają się pokazywać dopiero, gdy napis naprawdę widać. W czasie przejścia nowa strona leży już w drzewie,
    /// ale jest przezroczysta (ekran pokazuje efekt), więc składanie liczone od jej zbudowania przeleciałoby pod spodem —
    /// operator zobaczyłby napis już złożony. Stan sprawdzamy w klatce rysowania: chwilowe odsłonięcie strony na zrzut
    /// do przejścia dzieje się między klatkami i nic tu nie znaczy.
    /// </summary>
    private void StartWhenVisible()
    {
        if (_waiting is not { } delays || _reveal is not null || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var source = new CancellationTokenSource();
        _reveal = source;
        void Frame(TimeSpan now)
        {
            if (source.IsCancellationRequested)
                return;
            if (IsOnScreen)
                Start(delays, source.Token);
            else
                top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }

    /// <summary>Napis jest widoczny — nie zakrywa go ani ukryty, ani przezroczysty przodek (strona pod przejściem).</summary>
    private bool IsOnScreen
    {
        get
        {
            for (Visual? visual = this; visual is not null; visual = visual.GetVisualParent())
                if (!visual.IsVisible || visual.Opacity <= 0)
                    return false;
            return true;
        }
    }

    private void Start(IReadOnlyList<TimeSpan> delays, CancellationToken token)
    {
        _waiting = null;
        for (var i = 0; i < Children.Count; i++)
            _ = RevealAsync((TextBlock)Children[i], delays[i], token);
    }

    private static async Task RevealAsync(TextBlock word, TimeSpan delay, CancellationToken token)
    {
        var animation = new Animation
        {
            Duration = WordFade,
            Delay = delay,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };
        try
        {
            await animation.RunAsync(word, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        // animacja nie trzyma wartości po końcu (FillMode.Forward sięgałby ponad wartość lokalną)
        if (!token.IsCancellationRequested)
            word.Opacity = 1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Szerokość pola ogranicza pojedynczy wyraz: zbyt długi zawija się sam, reszta mieści się w swoim wierszu.
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        _space = MeasureSpace();
        var (width, height) = (0.0, 0.0);
        foreach (var line in Lines(availableSize.Width))
        {
            width = Math.Max(width, line.Width);
            height += line.Height;
        }
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (var line in Lines(finalSize.Width))
        {
            var x = HorizontalContentAlignment switch
            {
                HorizontalAlignment.Left => 0,
                HorizontalAlignment.Right => finalSize.Width - line.Width,
                _ => (finalSize.Width - line.Width) / 2,
            };
            for (var i = line.First; i <= line.Last; i++)
            {
                var child = Children[i];
                child.Arrange(new Rect(x, y + (line.Height - child.DesiredSize.Height), child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width + Space;
            }
            y += line.Height;
        }
        return finalSize;
    }

    /// <summary>Wyrównanie wierszy; domyślnie wyśrodkowane jak napis kiosku.</summary>
    public HorizontalAlignment HorizontalContentAlignment { get; set; } = HorizontalAlignment.Center;

    /// <summary>Odstęp między słowami — spacja w tym kroju i stopniu pisma (liczona przy mierzeniu).</summary>
    private double Space => _space;

    private double _space;

    /// <summary>
    /// Szerokość spacji: różnica między dwoma słowami rozdzielonymi spacją a sklejonymi — sama spacja na końcu tekstu
    /// nie ma zmierzonej szerokości, bo układ tekstu pomija końcowe odstępy.
    /// </summary>
    private double MeasureSpace() => TextWidth("i i") - TextWidth("ii");

    private double TextWidth(string text) => new FormattedText(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this), TextElement.GetFontWeight(this)),
        TextElement.GetFontSize(this),
        null).Width;

    private readonly record struct Line(int First, int Last, double Width, double Height);

    /// <summary>Podział słów na wiersze przy danej szerokości (twarde końce linii z tekstu zawsze łamią wiersz).</summary>
    private IEnumerable<Line> Lines(double width)
    {
        if (Children.Count == 0)
            yield break;
        var (first, lineWidth, lineHeight) = (0, 0.0, 0.0);
        for (var i = 0; i < Children.Count; i++)
        {
            var size = Children[i].DesiredSize;
            var next = lineWidth == 0 ? size.Width : lineWidth + Space + size.Width;
            if (i > first && (_breaks[i] || next > width))
            {
                yield return new Line(first, i - 1, lineWidth, lineHeight);
                (first, lineWidth, lineHeight) = (i, size.Width, size.Height);
                continue;
            }
            lineWidth = next;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        yield return new Line(first, Children.Count - 1, lineWidth, lineHeight);
    }
}
