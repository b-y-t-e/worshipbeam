// Port DeckCraft: crates/anim/src/lib.rs (AnimState, clampf) oraz Rgba z craft-drawingml (lerp, from_hex) — patrz UPSTREAM.md.
using System.Globalization;

namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Kolor sRGB z kanałem alfa (0–255), jak <c>Rgba</c> w DeckCraft.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Rgb(byte r, byte g, byte b) => new(r, g, b);

    /// <summary>Kolor pośredni (<paramref name="t"/> 0..1, poza zakresem przycięte, NaN = 0); kanały zaokrąglane jak w Rust (połówki od zera).</summary>
    public Rgba Lerp(Rgba to, double t)
    {
        var amount = double.IsFinite(t) ? Math.Clamp(t, 0.0, 1.0) : 0.0;
        byte Mix(byte from, byte end) => Channel(from + (end - (double)from) * amount);
        return new Rgba(Mix(R, to.R), Mix(G, to.G), Mix(B, to.B), Mix(A, to.A));
    }

    /// <summary><c>#RRGGBB</c>, <c>#RRGGBBAA</c>, <c>#RGB</c> (z <c>#</c> albo bez); <c>null</c>, gdy to nie kolor (np. <c>none</c>).</summary>
    public static Rgba? FromHex(string? hex)
    {
        if (hex is null)
            return null;
        var s = hex.Trim();
        if (s.StartsWith('#'))
            s = s[1..];
        if (!s.All(char.IsAsciiHexDigit))
            return null;
        byte Byte(int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte Nibble(int i) => (byte)(byte.Parse(s.AsSpan(i, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture) * 17);
        return s.Length switch
        {
            6 => new Rgba(Byte(0), Byte(2), Byte(4)),
            8 => new Rgba(Byte(0), Byte(2), Byte(4), Byte(6)),
            3 => new Rgba(Nibble(0), Nibble(1), Nibble(2)),
            _ => null,
        };
    }

    /// <summary><c>#RRGGBB</c> (z <c>AA</c>, gdy kolor nie jest w pełni kryjący).</summary>
    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    internal static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0.0, 255.0);
}

/// <summary>Pole kształtu w punktach.</summary>
public readonly record struct Xfrm(double X, double Y, double W, double H);

/// <summary>Odsłonięta część kształtu we współrzędnych kształtu 0..1 (lewo, góra, prawo, dół).</summary>
public readonly record struct ClipRect(double L, double T, double R, double B);

/// <summary>
/// Stan animacji kształtu (albo akapitu jego tekstu) w jednej chwili pokazu: przesunięcie w punktach, skala i obrót
/// (stopnie) wokół środka kształtu, odsłonięcie (<see cref="Clip"/>) i zastępczy kolor wypełnienia (<see cref="Tint"/>).
/// </summary>
public readonly record struct AnimState(bool Visible, double Opacity, double OffsetX, double OffsetY,
    double ScaleX, double ScaleY, double Rotate, ClipRect? Clip, Rgba? Tint)
{
    /// <summary>Nic do zastosowania.</summary>
    public static AnimState Identity { get; } = new(true, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, null, null);

    /// <summary>Kształt ukryty.</summary>
    public static AnimState Hidden { get; } = Identity with { Visible = false };

    public bool IsIdentity => this == Identity;

    /// <summary>Nakłada <paramref name="d"/> na ten stan: krycie i skale się mnożą, przesunięcia i obroty dodają, odsłonięcia przecinają, kolor zastępuje.</summary>
    public AnimState Compose(AnimState d)
    {
        var clip = (Clip, d.Clip) switch
        {
            ({ } a, { } b) => new ClipRect(Rs.Max(a.L, b.L), Rs.Max(a.T, b.T), Rs.Min(a.R, b.R), Rs.Min(a.B, b.B)),
            ({ } a, null) => a,
            (null, var b) => b,
        };
        return new AnimState(
            Visible && d.Visible,
            Opacity * d.Opacity,
            OffsetX + d.OffsetX,
            OffsetY + d.OffsetY,
            ScaleX * d.ScaleX,
            ScaleY * d.ScaleY,
            Rotate + d.Rotate,
            clip,
            d.Tint ?? Tint);
    }

    /// <summary>Liczby nieskończone i NaN zastępuje wartościami neutralnymi, przycina krycie i odsłonięcie.</summary>
    public AnimState Sanitized()
    {
        static double F(double v, double d) => double.IsFinite(v) ? v : d;
        ClipRect? clip = null;
        if (Clip is { } c)
        {
            var l = Math.Clamp(F(c.L, 0.0), 0.0, 1.0);
            var t = Math.Clamp(F(c.T, 0.0), 0.0, 1.0);
            var r = Math.Clamp(F(c.R, 1.0), 0.0, 1.0);
            var b = Math.Clamp(F(c.B, 1.0), 0.0, 1.0);
            clip = new ClipRect(l, t, Rs.Max(r, l), Rs.Max(b, t));
        }
        return this with
        {
            Opacity = Math.Clamp(F(Opacity, 1.0), 0.0, 1.0),
            OffsetX = F(OffsetX, 0.0),
            OffsetY = F(OffsetY, 0.0),
            ScaleX = F(ScaleX, 1.0),
            ScaleY = F(ScaleY, 1.0),
            Rotate = F(Rotate, 0.0),
            Clip = clip,
        };
    }
}

/// <summary>Funkcje liczbowe o semantyce Rust (<c>f64::min</c> / <c>max</c> pomijają NaN, <c>clampf</c> z lib.rs).</summary>
internal static class Rs
{
    /// <summary><c>clampf</c>: wrogą liczbę przycina do [<paramref name="lo"/>, <paramref name="hi"/>], NaN zamienia na <paramref name="nan"/>.</summary>
    public static double Clampf(double v, double lo, double hi, double nan) => double.IsNaN(v) ? nan : Math.Clamp(v, lo, hi);

    public static double Min(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Min(a, b);

    public static double Max(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
}
