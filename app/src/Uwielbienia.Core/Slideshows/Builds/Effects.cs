// Port DeckCraft: crates/anim/src/effects.rs — patrz UPSTREAM.md.
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Efekty, które port liczy osobno (reszta wpada w ogólne przenikanie, jak <c>_ =&gt;</c> w Rust).</summary>
public static class AnimationEffects
{
    /// <summary>Wejścia z własną gałęzią w <c>EffectSpec.entrance</c>.</summary>
    internal static readonly IReadOnlySet<string> Entrances = new HashSet<string>
    {
        "appear", "fade", "dissolve", "randomBars", "blinds", "checkerboard", "wheel", "fly", "float", "rise", "split", "wipe",
        "shape", "circle", "box", "diamond", "expandClip", "grow", "zoom", "swivel", "bounce", "peek", "strips", "expand", "spinner",
    };

    /// <summary>Wyróżnienia z własną gałęzią w <c>EffectSpec.emphasis</c>.</summary>
    internal static readonly IReadOnlySet<string> Emphases = new HashSet<string>
    {
        "pulse", "colorPulse", "teeter", "spin", "growShrink", "desaturate", "darken", "lighten", "transparency", "objectColor",
        "fillColor", "lineColor", "fontColor", "complementaryColor", "underline", "boldFlash", "wave",
    };

    /// <summary>Wyjścia nazwane inaczej niż wejście, któremu odpowiadają (<c>exit_base</c>).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ExitNames = new Dictionary<string, string>
    {
        ["disappear"] = "appear",
        ["fadeOut"] = "fade",
        ["flyOut"] = "fly",
        ["floatOut"] = "float",
        ["splitOut"] = "split",
        ["wipeOut"] = "wipe",
        ["shapeOut"] = "shape",
        ["wheelOut"] = "wheel",
        ["randomBarsOut"] = "randomBars",
        ["shrinkTurn"] = "grow",
        ["zoomOut"] = "zoom",
        ["swivelOut"] = "swivel",
        ["bounceOut"] = "bounce",
        ["dissolveOut"] = "dissolve",
        ["blindsOut"] = "blinds",
        ["boxOut"] = "box",
        ["checkerboardOut"] = "checkerboard",
        ["diamondOut"] = "diamond",
        ["peekOut"] = "peek",
        ["stripsOut"] = "strips",
        ["sinkDown"] = "rise",
        ["spinnerOut"] = "spinner",
        ["contract"] = "expand",
    };

    /// <summary>Gotowe ścieżki ruchu z <c>default_path</c> (poza <see cref="MotionPath.PresetPathEffects"/>) i własna ścieżka.</summary>
    internal static readonly IReadOnlySet<string> Paths = new HashSet<string>(
        new[] { "lines", "arcs", "turns", "shapes", "loops", "customPath" }.Concat(MotionPath.PresetPathEffects));

    /// <summary>Polecenia multimediów (stan kształtu się nie zmienia).</summary>
    internal static readonly IReadOnlySet<string> Media = new HashSet<string> { "play", "pause", "stop" };

    /// <summary>Każdy identyfikator efektu, który port liczy osobno: wejścia, nazwy wyjść, wyróżnienia, ścieżki, multimedia.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(
        Entrances.Concat(ExitNames.Keys).Concat(Emphases).Concat(Paths).Concat(Media));

    /// <summary><c>false</c> = efekt w tej klasie wpada w ogólną gałąź (wejście / wyjście — przenikanie, wyróżnienie — nic).</summary>
    public static bool IsKnown(string effect, AnimClass cls) => cls switch
    {
        AnimClass.Entrance => Entrances.Contains(effect),
        AnimClass.Exit => Entrances.Contains(ExitBase(effect)),
        AnimClass.Emphasis => Emphases.Contains(effect),
        AnimClass.Path => Paths.Contains(effect),
        AnimClass.Media => true,
        _ => false,
    };

    /// <summary><c>exit_base</c>: wejście, którego odwróceniem jest wyjście <paramref name="effect"/>.</summary>
    public static string ExitBase(string effect) => ExitNames.GetValueOrDefault(effect, effect);

    /// <summary><c>emphasis_persists</c>: wyróżnienia, których stan końcowy zostaje (obrót, rozmiar, kolor, przezroczystość).</summary>
    public static bool EmphasisPersists(string effect) => effect is "spin" or "growShrink" or "desaturate" or "darken" or "lighten"
        or "transparency" or "objectColor" or "complementaryColor" or "lineColor" or "fillColor" or "fontColor";

    internal static bool UsesDefaultSmoothing(string effect) =>
        effect is "fly" or "float" or "rise" or "peek" or "lines" or "arcs" or "turns" or "shapes" or "loops" or "customPath"
        || MotionPath.PresetPath(effect) is not null;

    /// <summary><c>motion_path</c>: własna ścieżka animacji albo domyślna gotowego efektu.</summary>
    internal static MotionPath MotionPathFor(string effect, string option, string? own) =>
        !string.IsNullOrWhiteSpace(own) ? MotionPath.Parse(own) : MotionPath.Parse(MotionPath.DefaultPath(effect, option));
}

/// <summary>Wszystko, czego efekt potrzebuje poza postępem (<c>EffectSpec</c>).</summary>
internal sealed record EffectSpec(
    AnimClass Class,
    // efekt bazowy (wyjścia zamienione na odpowiadające im wejścia)
    string Effect,
    string Option,
    double Accel,
    double Decel,
    double Bounce,
    double? Amount,
    Rgba? Color,
    MotionPath? Path,
    // pole kształtu w punktach (oczyszczone)
    Xfrm Xf,
    double SlideW,
    double SlideH)
{
    /// <summary>Domyślny kolor wyróżnień kolorem (ciemna czerwień), gdy animacja nie ma własnego.</summary>
    private static readonly Rgba DefaultTarget = Rgba.Rgb(0xC0, 0x00, 0x00);

    /// <summary>Postęp po wygładzeniu.</summary>
    public double Eased(double p) =>
        Accel <= 0.0 && Decel <= 0.0 && Bounce <= 0.0 && AnimationEffects.UsesDefaultSmoothing(Effect)
            ? Easing.Smooth(p)
            : Easing.Ease(p, Accel, Decel, Bounce);

    /// <summary>Zmiana stanu przy postępie liniowym <paramref name="p"/> (0..1); <paramref name="cur"/> = obecny kolor wypełnienia.</summary>
    public AnimState Eval(double p, Rgba cur)
    {
        p = Rs.Clampf(p, 0.0, 1.0, 0.0);
        var s = Class switch
        {
            AnimClass.Entrance => Entrance(p, false),
            AnimClass.Exit => Entrance(1.0 - p, true),
            AnimClass.Emphasis => Emphasis(p, cur),
            AnimClass.Path => Motion(p),
            _ => AnimState.Identity,
        };
        return s.Sanitized();
    }

    /// <summary>Stan, który efekt zostawia po zakończeniu.</summary>
    public AnimState EndState(Rgba cur) => Class switch
    {
        AnimClass.Entrance or AnimClass.Media => AnimState.Identity,
        AnimClass.Exit => AnimState.Hidden,
        AnimClass.Emphasis when AnimationEffects.EmphasisPersists(Effect) => Emphasis(1.0, cur).Sanitized(),
        AnimClass.Emphasis => AnimState.Identity,
        AnimClass.Path => Motion(1.0).Sanitized(),
        _ => AnimState.Identity,
    };

    private static ClipRect CenterClip(double f, bool horz, bool vert)
    {
        f = Math.Clamp(f, 0.0, 1.0) / 2.0;
        var (l, r) = horz ? (0.5 - f, 0.5 + f) : (0.0, 1.0);
        var (t, b) = vert ? (0.5 - f, 0.5 + f) : (0.0, 1.0);
        return new ClipRect(l, t, r, b);
    }

    private AnimState Entrance(double p, bool exit)
    {
        var e = Eased(p);
        var q = 1.0 - e;
        var o = Option;
        var (x, y, w, h) = (Xf.X, Xf.Y, Xf.W, Xf.H);
        var (sw, sh) = (SlideW, SlideH);
        var st = AnimState.Identity;
        switch (Effect)
        {
            case "appear":
                st = st with { Visible = p > 0.0 };
                break;
            case "fade" or "dissolve" or "randomBars" or "blinds" or "checkerboard" or "wheel":
                st = st with { Opacity = e };
                break;
            case "fly":
            {
                var dir = o.Length == 0 ? "b" : o;
                if (dir.Contains('l'))
                    st = st with { OffsetX = -(x + w) * q };
                if (dir.Contains('r'))
                    st = st with { OffsetX = (sw - x) * q };
                if (dir.Contains('t'))
                    st = st with { OffsetY = -(y + h) * q };
                if (dir.Contains('b'))
                    st = st with { OffsetY = (sh - y) * q };
                break;
            }
            case "float" or "rise":
            {
                // „Float Up” wchodzi od dołu i wychodzi w górę; „Float Down” odwrotnie
                var dir = o == "d" ? -1.0 : 1.0;
                if (exit)
                    dir = -dir;
                st = st with { OffsetY = dir * 0.1 * sh * q, Opacity = e };
                break;
            }
            case "split":
            {
                var horz = o.StartsWith("horz", StringComparison.Ordinal);
                // „In” zamyka się od brzegów: przybliżone odsłonięciem od środka z przenikaniem
                st = st with { Clip = horz ? CenterClip(e, false, true) : CenterClip(e, true, false) };
                if (o.EndsWith("In", StringComparison.Ordinal))
                    st = st with { Opacity = e };
                break;
            }
            case "wipe":
                st = st with
                {
                    Clip = o switch
                    {
                        "l" => new ClipRect(0.0, 0.0, e, 1.0),
                        "t" => new ClipRect(0.0, 0.0, 1.0, e),
                        "r" => new ClipRect(1.0 - e, 0.0, 1.0, 1.0),
                        _ => new ClipRect(0.0, 1.0 - e, 1.0, 1.0),
                    },
                };
                break;
            case "shape" or "circle" or "box" or "diamond" or "expandClip":
            {
                var round = Effect is "circle" or "diamond"
                    || o.StartsWith("circle", StringComparison.Ordinal)
                    || o.StartsWith("diamond", StringComparison.Ordinal)
                    || o.StartsWith("plus", StringComparison.Ordinal);
                var f = round ? Rs.Min(e * Math.Sqrt(2.0), 1.0) : e;
                st = st with { Clip = CenterClip(f, true, true) };
                if (o.EndsWith("In", StringComparison.Ordinal) || o == "in")
                    st = st with { Opacity = e };
                break;
            }
            case "grow":
                st = st with { ScaleX = e, ScaleY = e, Rotate = -90.0 * q, Opacity = e };
                break;
            case "zoom":
            {
                var s = o == "out" ? 1.0 + 3.0 * q : e;
                st = st with { ScaleX = s, ScaleY = s, Opacity = e };
                break;
            }
            case "swivel":
            {
                var s = e * Math.Cos(q * 2.5 * Math.PI);
                st = o == "vert" ? st with { ScaleY = s } : st with { ScaleX = s };
                st = st with { Opacity = Rs.Min(e, 1.0) };
                break;
            }
            case "bounce":
            {
                var b = Easing.BounceOut(p);
                st = st with
                {
                    OffsetY = -(0.25 * sh) * (1.0 - b),
                    OffsetX = -0.1 * sw * (1.0 - p),
                    Opacity = Rs.Min(p * 4.0, 1.0),
                };
                break;
            }
            case "peek":
                st = o switch
                {
                    "l" => st with { OffsetX = -w * q, Clip = new ClipRect(q, 0.0, 1.0, 1.0) },
                    "r" => st with { OffsetX = w * q, Clip = new ClipRect(0.0, 0.0, e, 1.0) },
                    "t" => st with { OffsetY = -h * q, Clip = new ClipRect(0.0, q, 1.0, 1.0) },
                    _ => st with { OffsetY = h * q, Clip = new ClipRect(0.0, 0.0, 1.0, e) },
                };
                break;
            case "strips":
                st = st with
                {
                    Clip = o switch
                    {
                        "ru" => new ClipRect(0.0, q, e, 1.0),
                        "ld" => new ClipRect(q, 0.0, 1.0, e),
                        "rd" => new ClipRect(0.0, 0.0, e, e),
                        _ => new ClipRect(q, q, 1.0, 1.0),
                    },
                };
                break;
            case "expand":
                st = st with { ScaleX = 0.3 + 0.7 * e, Opacity = e };
                break;
            case "spinner":
                st = st with { Rotate = 360.0 * q, ScaleX = 0.5 + 0.5 * e, ScaleY = 0.5 + 0.5 * e, Opacity = e };
                break;
            default:
                st = st with { Opacity = e };
                break;
        }
        return st;
    }

    private AnimState Emphasis(double p, Rgba cur)
    {
        var e = Eased(p);
        var bump = Math.Sin(p * Math.PI);
        var st = AnimState.Identity;
        var target = Color ?? DefaultTarget;
        switch (Effect)
        {
            case "pulse":
                st = st with { ScaleX = 1.0 + 0.1 * bump, ScaleY = 1.0 + 0.1 * bump };
                break;
            case "colorPulse":
                st = st with { Tint = cur.Lerp(target, bump) };
                break;
            case "teeter":
                st = st with { Rotate = 8.0 * Math.Sin(p * 4.0 * Math.PI) * (1.0 - p * 0.5) };
                break;
            case "spin":
            {
                var amt = Math.Clamp(Amount is { } a && double.IsFinite(a) ? a : 360.0, -36_000.0, 36_000.0);
                var sign = Option == "counterClockwise" ? -1.0 : 1.0;
                st = st with { Rotate = sign * amt * e };
                break;
            }
            case "growShrink":
            {
                var amt = Math.Clamp(Amount is { } a && double.IsFinite(a) && a > 0.0 ? a : 1.5, 0.0, 100.0);
                var s = 1.0 + (amt - 1.0) * e;
                st = Option switch
                {
                    "horz" => st with { ScaleX = s },
                    "vert" => st with { ScaleY = s },
                    _ => st with { ScaleX = s, ScaleY = s },
                };
                break;
            }
            case "desaturate":
            {
                var l = Rgba.Channel(0.299 * cur.R + 0.587 * cur.G + 0.114 * cur.B);
                st = st with { Tint = cur.Lerp(new Rgba(l, l, l, cur.A), e) };
                break;
            }
            case "darken":
                st = st with { Tint = cur.Lerp(new Rgba(0, 0, 0, cur.A), 0.4 * e) };
                break;
            case "lighten":
                st = st with { Tint = cur.Lerp(new Rgba(255, 255, 255, cur.A), 0.4 * e) };
                break;
            case "transparency":
            {
                // `amount` to przezroczystość (0.5 = 50 %)
                var a = Math.Clamp(Amount is { } v && double.IsFinite(v) ? v : 0.5, 0.0, 1.0);
                st = st with { Opacity = 1.0 - a * e };
                break;
            }
            case "objectColor" or "fillColor" or "lineColor" or "fontColor":
                st = st with { Tint = cur.Lerp(target, e) };
                break;
            case "complementaryColor":
                st = st with { Tint = cur.Lerp(new Rgba((byte)(255 - cur.R), (byte)(255 - cur.G), (byte)(255 - cur.B), cur.A), e) };
                break;
            case "underline" or "boldFlash":
                st = st with { ScaleX = 1.0 + 0.04 * bump, ScaleY = 1.0 + 0.04 * bump };
                if (Effect == "boldFlash")
                    st = st with { Tint = cur.Lerp(new Rgba(0, 0, 0, cur.A), 0.3 * bump) };
                break;
            case "wave":
                st = st with { OffsetY = -0.02 * SlideH * bump, ScaleY = 1.0 + 0.05 * bump };
                break;
        }
        return st;
    }

    private AnimState Motion(double p)
    {
        var e = Eased(p);
        var (fx, fy) = Path is { } mp ? mp.At(e) : (0.0, 0.0);
        return AnimState.Identity with { OffsetX = fx * SlideW, OffsetY = fy * SlideH };
    }
}
