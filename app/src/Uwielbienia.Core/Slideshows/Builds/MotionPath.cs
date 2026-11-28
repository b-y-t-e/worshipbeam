// Port DeckCraft: crates/anim/src/path.rs (+ preset_path z crates/model/src/anim.rs) — patrz UPSTREAM.md.
using System.Globalization;
using System.Text;

namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>
/// Ścieżka ruchu w składni podobnej do SVG (<c>M x y L x y C x1 y1 x2 y2 x y Q x1 y1 x y Z E</c>, małe litery =
/// względnie) w ułamkach slajdu, od miejsca, w którym stoi kształt. Położenie liczone wg długości łuku.
/// </summary>
public sealed class MotionPath
{
    private const int MaxTokens = 100_000;
    private const int MaxPoints = 20_000;
    private const int CurveSteps = 16;

    private readonly List<double> _cum;

    private MotionPath(List<(double X, double Y)> points, List<double> cum)
    {
        Points = points;
        _cum = cum;
    }

    /// <summary>Wierzchołki łamanej (ułamki slajdu).</summary>
    public IReadOnlyList<(double X, double Y)> Points { get; }

    private readonly record struct Tok(char Cmd, double Num, bool IsNum);

    private static List<Tok> Tokenize(string s)
    {
        var cs = s.Length > 1_000_000 ? s[..1_000_000] : s;
        var output = new List<Tok>();
        var i = 0;
        char At(int k) => k < cs.Length ? cs[k] : ' ';
        while (i < cs.Length && output.Count < MaxTokens)
        {
            var c = cs[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }
            if (char.IsAsciiDigit(c) || c == '.' || c == '-' || c == '+')
            {
                var start = i;
                i++;
                var seenDot = c == '.';
                while (i < cs.Length)
                {
                    var d = cs[i];
                    if (char.IsAsciiDigit(d))
                        i++;
                    else if (d == '.' && !seenDot)
                    {
                        seenDot = true;
                        i++;
                    }
                    else if ((d == 'e' || d == 'E')
                        && (char.IsAsciiDigit(At(i + 1)) || ((At(i + 1) == '-' || At(i + 1) == '+') && char.IsAsciiDigit(At(i + 2)))))
                    {
                        i += 2;
                        while (i < cs.Length && char.IsAsciiDigit(cs[i]))
                            i++;
                        break;
                    }
                    else
                        break;
                }
                var text = cs[start..Math.Min(i, cs.Length)];
                output.Add(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
                    ? new Tok('\0', v, true)
                    : new Tok('?', 0.0, false));
                continue;
            }
            output.Add(new Tok(c, 0.0, false));
            i++;
        }
        return output;
    }

    /// <summary>Wczytuje ścieżkę. Błędny zapis daje część wczytaną przed błędem (może być pusta).</summary>
    public static MotionPath Parse(string s)
    {
        var toks = Tokenize(s);
        var pts = new List<(double X, double Y)>();
        (double X, double Y) cur = (0.0, 0.0);
        (double X, double Y) start = (0.0, 0.0);
        var cmd = 'M';
        var i = 0;
        void Push((double X, double Y) p)
        {
            if (pts.Count < MaxPoints)
                pts.Add(p);
        }
        double? Num(int k) => k < toks.Count && toks[k].IsNum ? toks[k].Num : null;
        while (i < toks.Count)
        {
            if (!toks[i].IsNum)
            {
                cmd = toks[i].Cmd;
                i++;
                if (cmd is 'Z' or 'z')
                {
                    Push(start);
                    cur = start;
                    continue;
                }
                if (cmd is 'E' or 'e')
                    break;
                if (cmd is 'M' or 'm' or 'L' or 'l' or 'C' or 'c' or 'Q' or 'q' or 'H' or 'h' or 'V' or 'v')
                    continue;
                break;
            }
            var rel = char.IsAsciiLetterLower(cmd);
            (double X, double Y) b = rel ? cur : (0.0, 0.0);
            var upper = char.ToUpperInvariant(cmd);
            var need = upper switch
            {
                'M' or 'L' => 2,
                'C' => 6,
                'Q' => 4,
                'H' or 'V' => 1,
                _ => 0,
            };
            if (need == 0)
                break;
            var v = new double[6];
            var ok = true;
            for (var k = 0; k < need; k++)
            {
                if (Num(i + k) is { } x)
                    v[k] = x;
                else
                    ok = false;
            }
            if (!ok)
                break;
            i += need;
            switch (upper)
            {
                case 'M':
                    cur = (b.X + v[0], b.Y + v[1]);
                    start = cur;
                    // ruch wewnątrz ścieżki to skok; zostaje ciągłym odcinkiem
                    Push(cur);
                    cmd = rel ? 'l' : 'L';
                    break;
                case 'L':
                    if (pts.Count == 0)
                        Push(cur);
                    cur = (b.X + v[0], b.Y + v[1]);
                    Push(cur);
                    break;
                case 'H' or 'V':
                    if (pts.Count == 0)
                        Push(cur);
                    cur = upper == 'H' ? (b.X + v[0], cur.Y) : (cur.X, b.Y + v[0]);
                    Push(cur);
                    break;
                case 'C':
                {
                    if (pts.Count == 0)
                        Push(cur);
                    var p0 = cur;
                    (double X, double Y) p1 = (b.X + v[0], b.Y + v[1]);
                    (double X, double Y) p2 = (b.X + v[2], b.Y + v[3]);
                    (double X, double Y) p3 = (b.X + v[4], b.Y + v[5]);
                    for (var k = 1; k <= CurveSteps; k++)
                    {
                        var t = k / (double)CurveSteps;
                        var u = 1.0 - t;
                        var ca = u * u * u;
                        var cb = 3.0 * u * u * t;
                        var cc = 3.0 * u * t * t;
                        var cd = t * t * t;
                        Push((ca * p0.X + cb * p1.X + cc * p2.X + cd * p3.X, ca * p0.Y + cb * p1.Y + cc * p2.Y + cd * p3.Y));
                    }
                    cur = p3;
                    break;
                }
                case 'Q':
                {
                    if (pts.Count == 0)
                        Push(cur);
                    var p0 = cur;
                    (double X, double Y) p1 = (b.X + v[0], b.Y + v[1]);
                    (double X, double Y) p2 = (b.X + v[2], b.Y + v[3]);
                    for (var k = 1; k <= CurveSteps; k++)
                    {
                        var t = k / (double)CurveSteps;
                        var u = 1.0 - t;
                        Push((u * u * p0.X + 2.0 * u * t * p1.X + t * t * p2.X, u * u * p0.Y + 2.0 * u * t * p1.Y + t * t * p2.Y));
                    }
                    cur = p2;
                    break;
                }
            }
        }
        pts.RemoveAll(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y));
        var cum = new List<double>(pts.Count);
        var acc = 0.0;
        (double X, double Y)? prev = null;
        foreach (var p in pts)
        {
            if (prev is { } q)
            {
                var dx = p.X - q.X;
                var dy = p.Y - q.Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (double.IsFinite(d))
                    acc += d;
            }
            cum.Add(acc);
            prev = p;
        }
        return new MotionPath(pts, cum);
    }

    /// <summary>Długość łuku (ułamki slajdu).</summary>
    public double Length => _cum.Count > 0 ? _cum[^1] : 0.0;

    /// <summary>Położenie przy ułamku długości <paramref name="p"/> (0..1), względem początku ścieżki (0, 0).</summary>
    public (double X, double Y) At(double p)
    {
        var first = Points.Count > 0 ? Points[0] : (0.0, 0.0);
        var len = Length;
        if (double.IsNaN(len) || len <= 0.0 || !double.IsFinite(len))
            return first;
        p = Rs.Clampf(p, 0.0, 1.0, 0.0);
        var target = p * len;
        var i = PartitionPoint(target);
        if (i == 0)
            return first;
        if (i >= Points.Count || i >= _cum.Count)
            return Points.Count > 0 ? Points[^1] : first;
        var a = Points[i - 1];
        var b = Points[i];
        var ca = _cum[i - 1];
        var seg = _cum[i] - ca;
        var f = seg > 0.0 ? (target - ca) / seg : 0.0;
        return (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
    }

    /// <summary>Pierwszy indeks, dla którego długość narastająca nie jest mniejsza od <paramref name="target"/>.</summary>
    private int PartitionPoint(double target)
    {
        int lo = 0, hi = _cum.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_cum[mid] < target)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static string F4(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    private static string Polygon(int n, double r, double rot0)
    {
        // wielokąt foremny przez (0, 0), ze środkiem pod nim
        var s = new StringBuilder("M 0 0");
        (double X, double Y) c = (0.0, r);
        var a0 = -Math.PI / 2 + rot0;
        (double X, double Y) start = (c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        for (var k = 1; k <= n; k++)
        {
            var a = a0 + Math.Tau * k / n;
            s.Append(" L ").Append(F4(c.X + r * Math.Cos(a) - start.X)).Append(' ').Append(F4(c.Y + r * Math.Sin(a) - start.Y));
        }
        s.Append(" E");
        return s.ToString();
    }

    /// <summary><c>default_path</c>: ścieżka gotowego efektu ruchu, gdy animacja nie ma własnej.</summary>
    public static string DefaultPath(string effect, string option)
    {
        var (sx, sy, swap) = option switch
        {
            "up" => (1.0, -1.0, false),
            "right" => (1.0, 1.0, true),
            "left" => (-1.0, 1.0, true),
            _ => (1.0, 1.0, false),
        };
        // ścieżki bazowe idą w dół; `swap` kładzie je poziomo
        List<(double X, double Y)> Tr(params (double X, double Y)[] pts) =>
            pts.Select(p => swap ? (p.Y * sx, p.X * sy) : (p.X * sx, p.Y * sy)).ToList();
        string Fmt(string head, List<(double X, double Y)> pts)
        {
            var s = new StringBuilder(head);
            foreach (var (x, y) in pts)
                s.Append(' ').Append(F4(x)).Append(' ').Append(F4(y));
            s.Append(" E");
            return s.ToString();
        }
        switch (effect)
        {
            case "lines":
                return Fmt("M 0 0 L", Tr((0.0, 0.25)));
            case "arcs":
                return Fmt("M 0 0 C", Tr((0.1, 0.05), (0.1, 0.2), (0.0, 0.25)));
            case "turns":
            {
                var p = Tr((0.0, 0.12), (0.0, 0.2), (0.05, 0.25), (0.12, 0.25));
                var s = new StringBuilder("M 0 0");
                s.Append(" L ").Append(F4(p[0].X)).Append(' ').Append(F4(p[0].Y)).Append(" C");
                foreach (var (x, y) in p.Skip(1))
                    s.Append(' ').Append(F4(x)).Append(' ').Append(F4(y));
                s.Append(" E");
                return s.ToString();
            }
            case "shapes":
                return option switch
                {
                    "diamond" => Polygon(4, 0.1, 0.0),
                    "hexagon" => Polygon(6, 0.1, 0.0),
                    "triangle" => Polygon(3, 0.1, 0.0),
                    "square" => Polygon(4, 0.1, Math.PI / 4),
                    "octagon" => Polygon(8, 0.1, 0.0),
                    "pentagon" => Polygon(5, 0.1, 0.0),
                    "trapezoid" => "M 0 0 L 0.1 0 L 0.15 0.15 L -0.05 0.15 Z E",
                    "parallelogram" => "M 0 0 L 0.15 0 L 0.1 0.15 L -0.05 0.15 Z E",
                    _ => "M 0 0 C 0.0552 0 0.1 0.0448 0.1 0.1 C 0.1 0.1552 0.0552 0.2 0 0.2 C -0.0552 0.2 -0.1 0.1552 -0.1 0.1 C -0.1 0.0448 -0.0552 0 0 0 Z E",
                };
            case "loops":
                return "M 0 0 C 0.08 -0.1 0.16 0 0.08 0.05 C 0 0.1 -0.08 0.1 -0.08 0 C -0.08 -0.1 0 -0.05 0 0 E";
            default:
                return PresetPath(effect) ?? "";
        }
    }

    /// <summary>Nazwy efektów, które ma <see cref="PresetPath"/>.</summary>
    public static IReadOnlyList<string> PresetPathEffects { get; } =
    [
        "diagonalDownRight", "diagonalUpRight", "zigzag", "sineWave", "pathWave", "decayingWave", "heartbeat", "sCurve1",
        "sCurve2", "stairsDown", "bounceRight", "bounceLeft", "spiralRight", "spiralLeft", "spring", "funnel", "swoosh", "star4",
        "star5", "star6", "star8", "heart", "crescentMoon", "teardrop", "football", "plusPath", "rightTriangle", "curvedSquare",
        "figure8Horizontal", "figure8Vertical", "loopDeLoop", "bean", "peanut", "neutron",
    ];

    /// <summary>
    /// <c>preset_path</c> (model/anim.rs): ścieżka gotowego efektu ruchu poza Lines, Arcs, Turns, Shapes i Loops, w ułamkach
    /// slajdu od miejsca kształtu; <c>null</c> dla innego efektu.
    /// </summary>
    public static string? PresetPath(string effect)
    {
        const double pi = Math.PI;
        const double tau = Math.Tau;
        // próbkuje `f` w 0..=1 do łamanej od (0, 0)
        static string Poly(int n, bool closed, Func<double, (double X, double Y)> f)
        {
            var (x0, y0) = f(0.0);
            var s = new StringBuilder("M 0 0");
            for (var k = 1; k <= n; k++)
            {
                var (x, y) = f(k / (double)n);
                s.Append(" L ").Append(F4(x - x0)).Append(' ').Append(F4(y - y0));
            }
            if (closed)
                s.Append(" Z");
            s.Append(" E");
            return s.ToString();
        }
        static string Pts(IEnumerable<(double X, double Y)> p, bool closed)
        {
            var s = new StringBuilder("M 0 0");
            foreach (var (x, y) in p)
                s.Append(" L ").Append(F4(x)).Append(' ').Append(F4(y));
            if (closed)
                s.Append(" Z");
            s.Append(" E");
            return s.ToString();
        }
        // krzywa zamknięta we współrzędnych biegunowych r(θ), od góry
        static string Polar(double scale, Func<double, double> r) => Poly(96, true, t =>
        {
            var a = -pi / 2.0 + tau * t;
            var rr = r(a + pi / 2.0) * scale;
            return (rr * Math.Cos(a), rr * Math.Sin(a));
        });
        static string Star(int n, double outer, double inner)
        {
            var p = new List<(double X, double Y)>();
            (double X, double Y) start = (0.0, -outer);
            for (var k = 1; k <= 2 * n; k++)
            {
                var a = -pi / 2.0 + pi * k / n;
                var r = k % 2 == 1 ? inner : outer;
                p.Add((r * Math.Cos(a) - start.X, r * Math.Sin(a) - start.Y));
            }
            return Pts(p, true);
        }
        static string Spiral(double dir) => Poly(120, false, t =>
        {
            var r = 0.12 * (1.0 - t);
            var a = dir * 3.0 * tau * t;
            return (dir * (0.12 - r * Math.Cos(a)), -r * Math.Sin(a));
        });
        static string Bounce(double dir) =>
            Poly(90, false, t => (dir * 0.3 * t, 0.15 * t - 0.1 * (1.0 - t) * Math.Abs(Math.Sin(3.0 * pi * t))));
        static double Cube(double v) => v * v * v;
        return effect switch
        {
            "diagonalDownRight" => Pts([(0.2, 0.2)], false),
            "diagonalUpRight" => Pts([(0.2, -0.2)], false),
            "zigzag" => Pts([(0.05, 0.04), (0.1, 0.0), (0.15, 0.04), (0.2, 0.0), (0.25, 0.04), (0.3, 0.0)], false),
            "sineWave" => Poly(64, false, t => (0.3 * t, 0.04 * Math.Sin(2.0 * tau * t))),
            "pathWave" => Poly(48, false, t => (0.3 * t, 0.06 * Math.Sin(tau * t))),
            "decayingWave" => Poly(96, false, t => (0.3 * t, 0.07 * Math.Exp(-3.0 * t) * Math.Sin(3.0 * tau * t))),
            "heartbeat" => Pts([(0.08, 0.0), (0.1, -0.08), (0.13, 0.06), (0.15, 0.0), (0.25, 0.0)], false),
            "sCurve1" => Poly(48, false, t => (0.2 * t, 0.1 - 0.1 * Math.Cos(pi * t))),
            "sCurve2" => Poly(48, false, t => (0.1 - 0.1 * Math.Cos(pi * t), 0.2 * t)),
            "stairsDown" => Pts([(0.06, 0.0), (0.06, 0.06), (0.12, 0.06), (0.12, 0.12), (0.18, 0.12), (0.18, 0.18)], false),
            "bounceRight" => Bounce(1.0),
            "bounceLeft" => Bounce(-1.0),
            "spiralRight" => Spiral(1.0),
            "spiralLeft" => Spiral(-1.0),
            "spring" => Poly(160, false, t => (0.3 * t + 0.03 * Math.Cos(5.0 * tau * t), 0.03 * Math.Sin(5.0 * tau * t))),
            "funnel" => Poly(160, false, t =>
            {
                var r = 0.12 * (1.0 - t);
                return (r * Math.Cos(3.0 * tau * t), 0.25 * t + 0.03 * Math.Sin(3.0 * tau * t) * (1.0 - t));
            }),
            "swoosh" => Poly(48, false, t => (0.3 * t, -0.12 * Math.Sin(pi * t) + 0.1 * t * t)),
            "star4" => Star(4, 0.1, 0.04),
            "star5" => Star(5, 0.1, 0.04),
            "star6" => Star(6, 0.1, 0.05),
            "star8" => Star(8, 0.1, 0.06),
            "heart" => Poly(96, true, t =>
            {
                var a = tau * t;
                return (0.006 * 16.0 * Cube(Math.Sin(a)),
                    -0.006 * (13.0 * Math.Cos(a) - 5.0 * Math.Cos(2.0 * a) - 2.0 * Math.Cos(3.0 * a) - Math.Cos(4.0 * a)));
            }),
            "crescentMoon" => Pts(
                Enumerable.Range(1, 32).Select(k =>
                {
                    var a = -pi / 2.0 - pi * k / 32.0;
                    return (0.1 * Math.Cos(a), 0.1 + 0.1 * Math.Sin(a));
                }).Concat(Enumerable.Range(1, 32).Select(k =>
                {
                    var a = pi / 2.0 - pi * k / 32.0;
                    return (0.06 * Math.Cos(a) - 0.03, 0.1 + 0.1 * Math.Sin(a));
                })).ToList(),
                true),
            "teardrop" => Polar(0.1, a => 0.6 + 0.4 * Math.Pow(Math.Abs(Math.Cos(a / 2.0)), 3.0)),
            "football" => Poly(64, true, t =>
            {
                var a = tau * t;
                return (0.12 * Math.Sin(a), 0.06 * (1.0 - Math.Cos(a)) * (1.0 - 0.3 * Math.Abs(Math.Cos(2.0 * a))));
            }),
            "plusPath" => Pts(
                [
                    (0.04, 0.0), (0.04, 0.04), (0.08, 0.04), (0.08, 0.08), (0.04, 0.08), (0.04, 0.12), (0.0, 0.12), (0.0, 0.08),
                    (-0.04, 0.08), (-0.04, 0.04), (0.0, 0.04),
                ],
                true),
            "rightTriangle" => Pts([(0.0, 0.15), (0.15, 0.15)], true),
            "curvedSquare" => Poly(96, true, t =>
            {
                var a = -pi / 2.0 + tau * t;
                static double Sg(double v) => Math.CopySign(1.0, v) * Math.Sqrt(Math.Abs(v));
                return (0.1 * Sg(Math.Cos(a)), 0.1 + 0.1 * Sg(Math.Sin(a)));
            }),
            "figure8Horizontal" => Poly(96, true, t =>
            {
                var a = tau * t;
                return (0.15 * Math.Sin(a), 0.06 * Math.Sin(2.0 * a));
            }),
            "figure8Vertical" => Poly(96, true, t =>
            {
                var a = tau * t;
                return (0.06 * Math.Sin(2.0 * a), 0.15 * (1.0 - Math.Cos(a)));
            }),
            "loopDeLoop" => Poly(120, false, t => (0.3 * t - 0.05 * Math.Sin(2.0 * tau * t), -0.05 * (1.0 - Math.Cos(2.0 * tau * t)))),
            "bean" => Polar(0.1, a => 0.75 + 0.25 * Math.Cos(a) + 0.15 * Math.Cos(2.0 * a)),
            "peanut" => Polar(0.1, a => 0.7 + 0.3 * Math.Abs(Math.Cos(2.0 * a))),
            "neutron" => Polar(0.1, a => 0.35 + 0.65 * Math.Abs(Math.Cos(3.0 * a))),
            _ => null,
        };
    }
}
