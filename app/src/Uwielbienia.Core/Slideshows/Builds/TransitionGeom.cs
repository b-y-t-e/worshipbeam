// Port of DeckCraft crates/anim/src/transition_geom.rs (with private copies of easing.rs ease/smooth and lib.rs clampf).
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Punkt sceny 3-D: x w prawo i y w dół w wysokościach slajdu (slajd: x ∈ [−a/2, a/2], y ∈ [−½, ½] przy z = 0), z od widza.</summary>
internal readonly record struct V3(double X, double Y, double Z)
{
    public V3 Add(V3 o) => new(X + o.X, Y + o.Y, Z + o.Z);

    public V3 Sub(V3 o) => new(X - o.X, Y - o.Y, Z - o.Z);

    /// <summary>Obrót wokół osi pionowej przez <paramref name="c"/> (stopnie; dodatni odwraca prawą stronę od widza).</summary>
    public V3 Yaw(V3 c, double deg)
    {
        var (s, co) = Math.SinCos(TransitionGeom.Radians(deg));
        var p = Sub(c);
        return new V3(p.X * co - p.Z * s, p.Y, p.X * s + p.Z * co).Add(c);
    }

    /// <summary>Obrót wokół osi poziomej przez <paramref name="c"/> (stopnie; dodatni odwraca górę od widza).</summary>
    public V3 Pitch(V3 c, double deg)
    {
        var (s, co) = Math.SinCos(TransitionGeom.Radians(deg));
        var p = Sub(c);
        return new V3(p.X, p.Y * co + p.Z * s, -p.Y * s + p.Z * co).Add(c);
    }

    /// <summary>Obrót w płaszczyźnie ekranu wokół <paramref name="c"/> (stopnie, zgodnie z ruchem wskazówek).</summary>
    public V3 Roll(V3 c, double deg)
    {
        var (s, co) = Math.SinCos(TransitionGeom.Radians(deg));
        var p = Sub(c);
        return new V3(p.X * co - p.Y * s, p.X * s + p.Y * co, p.Z).Add(c);
    }
}

/// <summary>Kamera perspektywiczna patrząca na płaszczyznę slajdu z odległości <paramref name="D"/> wysokości slajdu.</summary>
internal readonly record struct Cam(double A, double D)
{
    public UvPoint P(V3 v)
    {
        var k = D / TransitionGeom.FMax(D + v.Z, D * 0.02);
        return new UvPoint(v.X * k / A + 0.5, v.Y * k + 0.5);
    }

    /// <summary>Narożniki 3-D (tl, tr, br, bl) prostokąta <paramref name="r"/> leżącego na płaszczyźnie slajdu.</summary>
    public V3[] Rect(double[] r)
    {
        var a = A;
        return TransitionLayer.Corners(r).Select(p => new V3((p.X - 0.5) * a, p.Y - 0.5, 0.0)).ToArray();
    }

    /// <summary>Punkt 3-D punktu (x, y) slajdu.</summary>
    public V3 Unit(double x, double y) => new((x - 0.5) * A, y - 0.5, 0.0);

    public UvPoint[] Quad(V3[] c)
    {
        var cam = this;
        return c.Select(cam.P).ToArray();
    }
}

/// <summary>Geometria efektów przejść: krzywe, losowość, karty 3-D, pola krycia, wielokąty (<c>transition_geom.rs</c>).</summary>
internal static class TransitionGeom
{
    // ---------- liczby (lib.rs, easing.rs) ----------

    /// <summary>Wartość w [lo, hi], NaN → <paramref name="nan"/> (<c>clampf</c>).</summary>
    public static double ClampF(double v, double lo, double hi, double nan) => double.IsNaN(v) ? nan : Math.Clamp(v, lo, hi);

    /// <summary><c>f64::min</c> — NaN po jednej stronie daje drugą liczbę (Math.Min dałby NaN).</summary>
    public static double FMin(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Min(a, b);

    /// <summary><c>f64::max</c> — NaN po jednej stronie daje drugą liczbę.</summary>
    public static double FMax(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);

    public static double Min(IEnumerable<double> v) => v.Aggregate(double.PositiveInfinity, FMin);

    public static double Max(IEnumerable<double> v) => v.Aggregate(double.NegativeInfinity, FMax);

    /// <summary><c>f64::to_radians</c>.</summary>
    public static double Radians(double deg) => deg * (Math.PI / 180.0);

    /// <summary><c>f64::rem_euclid</c>.</summary>
    public static double RemEuclid(double a, double b)
    {
        var r = a % b;
        return r < 0.0 ? r + Math.Abs(b) : r;
    }

    /// <summary>Liczba bajtów UTF-8 (<c>str::len</c> w Ruście — opcje porównuje się po długości).</summary>
    public static int ByteLen(string s) => System.Text.Encoding.UTF8.GetByteCount(s);

    /// <summary>Krzywa w stylu PowerPointa — wspólna z animacjami (<see cref="Easing.Ease"/>, <c>easing.rs</c>).</summary>
    public static double Ease(double p, double accel, double decel, double bounce) => Easing.Ease(p, accel, decel, bounce);

    /// <summary>Gładkie wejście i wyjście (smoothstep) — <see cref="Easing.Smooth"/>.</summary>
    public static double Smooth(double p) => Easing.Smooth(p);

    // ---------- krzywe ----------

    /// <summary>Smoothstep (przenikania PowerPointa).</summary>
    public static double Ss(double x) => Smooth(x);

    /// <summary>Kwadratowe wejście i wyjście (ruchy PowerPointa: Wypychanie, Zakrywanie, Odsłanianie, Panorama…).</summary>
    public static double Q2(double x) => Ease(x, 0.5, 0.5, 0.0);

    /// <summary>Potęgowe wejście i wyjście stopnia <paramref name="n"/>.</summary>
    public static double PowInOut(double x, double n)
    {
        x = Math.Clamp(x, 0.0, 1.0);
        return x < 0.5 ? 0.5 * Math.Pow(2.0 * x, n) : 1.0 - 0.5 * Math.Pow(2.0 - 2.0 * x, n);
    }

    /// <summary>Kwadratowe wejście.</summary>
    public static double EIn(double x)
    {
        x = Math.Clamp(x, 0.0, 1.0);
        return x * x;
    }

    /// <summary>Kwadratowe wyjście.</summary>
    public static double EOut(double x)
    {
        x = 1.0 - Math.Clamp(x, 0.0, 1.0);
        return 1.0 - x * x;
    }

    /// <summary>Postęp przez okno [a, b] czasu t, w 0..1.</summary>
    public static double Win(double t, double a, double b)
    {
        if (b <= a)
            return t >= b ? 1.0 : 0.0;
        return Math.Clamp((t - a) / (b - a), 0.0, 1.0);
    }

    // ---------- losowość ----------

    public static ulong Hash64(ulong x)
    {
        unchecked
        {
            x += 0x9E37_79B9_7F4A_7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D0_49BB_1331_11EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>Powtarzalna wartość pseudolosowa w [0, 1).</summary>
    public static double Rnd(ulong i) => (Hash64(i) >> 11) / (double)(1UL << 53);

    // ---------- 2-D ----------

    public static double[] Offset(double[] r, double dx, double dy) => [r[0] + dx, r[1] + dy, r[2] + dx, r[3] + dy];

    public static UvPoint Center(double[] r) => new((r[0] + r[2]) / 2.0, (r[1] + r[3]) / 2.0);

    public static double[] ScaleRect(double[] r, double s) => ScaleRectAbout(r, s, Center(r));

    public static double[] ScaleRectAbout(double[] r, double s, UvPoint c) =>
        [c.X + (r[0] - c.X) * s, c.Y + (r[1] - c.Y) * s, c.X + (r[2] - c.X) * s, c.Y + (r[3] - c.Y) * s];

    /// <summary>Całe źródło przesunięte o (dx, dy).</summary>
    public static TransitionLayer Moved(TransitionSource source, double dx, double dy, double alpha) =>
        TransitionLayer.Rect(source, TransitionLayer.FullRect, Offset(TransitionLayer.FullRect, dx, dy), alpha);

    /// <summary>Całe źródło przeskalowane o <paramref name="s"/> wokół (cx, cy) i przesunięte.</summary>
    public static TransitionLayer Scaled(TransitionSource source, double s, double cx, double cy, double dx, double dy, double alpha) =>
        TransitionLayer.Rect(source, TransitionLayer.FullRect, Offset(ScaleRectAbout(TransitionLayer.FullRect, s, new UvPoint(cx, cy)), dx, dy), alpha);

    /// <summary>Obrót punktu wokół <paramref name="c"/> o <paramref name="deg"/> (zgodnie z ruchem wskazówek) na slajdzie <paramref name="a"/> szerokim na jednostkę wysokości.</summary>
    public static UvPoint Rot(UvPoint p, UvPoint c, double deg, double a)
    {
        var (s, co) = Math.SinCos(Radians(deg));
        var (x, y) = ((p.X - c.X) * a, p.Y - c.Y);
        return new UvPoint(c.X + (x * co - y * s) / a, c.Y + x * s + y * co);
    }

    public static UvPoint[] RotQuad(UvPoint[] q, UvPoint c, double deg, double a) =>
        [Rot(q[0], c, deg, a), Rot(q[1], c, deg, a), Rot(q[2], c, deg, a), Rot(q[3], c, deg, a)];

    /// <summary>Kierunek ruchu z opcji: l, r, u, d i przekątne.</summary>
    public static (double X, double Y) Dir(string opt, (double, double) @default)
    {
        var d = (X: 0.0, Y: 0.0);
        if (opt.Contains('l'))
            d.X = -1.0;
        if (opt.Contains('r'))
            d.X = 1.0;
        if (opt.Contains('u'))
            d.Y = -1.0;
        if (opt.Contains('d'))
            d.Y = 1.0;
        return d == (0.0, 0.0) ? @default : d;
    }

    /// <summary>
    /// Prostokąt z miękkim brzegiem szerokim na <paramref name="f"/> (jednostki slajdu), zanikającym do zera:
    /// prostokąt w pełnym kryciu, cztery rampy krawędzi i cztery narożników.
    /// </summary>
    public static List<TransitionLayer> SoftRect(TransitionSource source, double[] src, double[] dst, double f, double alpha)
    {
        var v = new List<TransitionLayer> { TransitionLayer.Rect(source, src, dst, alpha) };
        if (f <= 1e-6)
            return v;
        var (l, t, r, b) = (dst[0], dst[1], dst[2], dst[3]);
        var (fx, fy) = (f, f * 16.0 / 9.0);
        UvPoint UvAt(double x, double y)
        {
            var u = r > l ? src[0] + (Math.Clamp(x, l, r) - l) / (r - l) * (src[2] - src[0]) : src[0];
            var w = b > t ? src[1] + (Math.Clamp(y, t, b) - t) / (b - t) * (src[3] - src[1]) : src[1];
            return new UvPoint(u, w);
        }
        void Q(UvPoint[] pts, double[] fade)
        {
            var uv = pts.Select(p => UvAt(p.X, p.Y)).ToArray();
            v.Add(new TransitionLayer(source, [Min(uv.Select(p => p.X)), Min(uv.Select(p => p.Y)), Max(uv.Select(p => p.X)), Max(uv.Select(p => p.Y))],
                pts, uv, alpha, fade));
        }
        Q([new(l, t - fy), new(r, t - fy), new(r, t), new(l, t)], [0.0, 0.0, 1.0, 1.0]);
        Q([new(l, b), new(r, b), new(r, b + fy), new(l, b + fy)], [1.0, 1.0, 0.0, 0.0]);
        Q([new(l - fx, t), new(l, t), new(l, b), new(l - fx, b)], [0.0, 1.0, 1.0, 0.0]);
        Q([new(r, t), new(r + fx, t), new(r + fx, b), new(r, b)], [1.0, 0.0, 0.0, 1.0]);
        Q([new(l - fx, t - fy), new(l, t - fy), new(l, t), new(l - fx, t)], [0.0, 0.0, 1.0, 0.0]);
        Q([new(r, t - fy), new(r + fx, t - fy), new(r + fx, t), new(r, t)], [0.0, 0.0, 0.0, 1.0]);
        Q([new(r, b), new(r + fx, b), new(r + fx, b + fy), new(r, b + fy)], [1.0, 0.0, 0.0, 0.0]);
        Q([new(l - fx, b), new(l, b), new(l, b + fy), new(l - fx, b + fy)], [0.0, 1.0, 0.0, 0.0]);
        return v;
    }

    // ---------- pola krycia ----------

    /// <summary>
    /// Źródło pokazane w miejscu przez pole krycia <paramref name="f"/>(x, y) (0..1) próbkowane na siatce
    /// <paramref name="n"/> × <paramref name="m"/>: każda komórka to czworokąt z kryciem narożników. Komórki bez treści
    /// pomijamy, a ciągi w pełni kryjących komórek scalamy.
    /// </summary>
    public static List<TransitionLayer> Field(TransitionSource source, int n, int m, Func<double, double, double> f)
    {
        (n, m) = (Math.Clamp(n, 1, 256), Math.Clamp(m, 1, 256));
        var vals = new double[(n + 1) * (m + 1)];
        for (var j = 0; j <= m; j++)
            for (var i = 0; i <= n; i++)
            {
                var v = f(i / (double)n, j / (double)m);
                vals[j * (n + 1) + i] = double.IsFinite(v) ? Math.Clamp(v, 0.0, 1.0) : 0.0;
            }
        double At(int i, int j) => vals[j * (n + 1) + i];
        var output = new List<TransitionLayer>();
        for (var j = 0; j < m; j++)
        {
            var (y0, y1) = (j / (double)m, (j + 1) / (double)m);
            int? run = null;
            for (var i = 0; i <= n; i++)
            {
                var full = i < n && At(i, j) >= 1.0 && At(i + 1, j) >= 1.0 && At(i, j + 1) >= 1.0 && At(i + 1, j + 1) >= 1.0;
                if (full)
                {
                    run ??= i;
                    continue;
                }
                if (run is { } s)
                {
                    run = null;
                    output.Add(TransitionLayer.InPlace(source, [s / (double)n, y0, i / (double)n, y1], 1.0));
                }
                if (i == n)
                    break;
                double[] c = [At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1)];
                if (c.All(x => x <= 0.0))
                    continue;
                output.Add(TransitionLayer.InPlace(source, [i / (double)n, y0, (i + 1) / (double)n, y1], 1.0).Faded(c));
            }
        }
        return output;
    }

    /// <summary>Krycie miękkiej krawędzi w miejscu <paramref name="p"/>, gdy jej środek jest w <paramref name="c"/>, a rampa ma szerokość <paramref name="w"/>.</summary>
    public static double Edge(double c, double p, double w)
    {
        if (w <= 1e-9)
            return p <= c ? 1.0 : 0.0;
        return Math.Clamp((c - p) / w + 0.5, 0.0, 1.0);
    }

    /// <summary>Punkt na brzegu kwadratu jednostkowego widziany z jego środka pod kątem <paramref name="ang"/> (radiany od godziny 12, zgodnie z ruchem wskazówek, na slajdzie <paramref name="a"/>).</summary>
    public static UvPoint Boundary(double ang, double a)
    {
        var (dx, dy) = (Math.Sin(ang) / a, -Math.Cos(ang));
        var m = FMax(FMax(Math.Abs(dx) / 0.5, Math.Abs(dy) / 0.5), 1e-9);
        return new UvPoint(0.5 + dx / m, 0.5 + dy / m);
    }

    /// <summary>Źródło w miejscu przez kątowe pole krycia <paramref name="f"/>(kąt): wachlarz cienkich klinów ze środka.</summary>
    public static List<TransitionLayer> Fan(TransitionSource source, double a, int steps, Func<double, double> f)
    {
        var n = Math.Clamp(steps, 8, 720);
        var angles = Enumerable.Range(0, n + 1).Select(k => 2.0 * Math.PI * k / n).ToList();
        // narożniki kwadratu — kliny mają do nich sięgać
        foreach (var (cx, cy) in new[] { (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.0, 0.0) })
            angles.Add(RemEuclid(Math.Atan2((cx - 0.5) * a, -(cy - 0.5)), 2.0 * Math.PI));
        angles.Sort((x, y) => x.CompareTo(y));
        var unique = new List<double>();
        foreach (var x in angles)
            if (unique.Count == 0 || Math.Abs(x - unique[^1]) >= 1e-9)
                unique.Add(x);
        var output = new List<TransitionLayer>();
        var c = new UvPoint(0.5, 0.5);
        for (var i = 0; i + 1 < unique.Count; i++)
        {
            var (x, y) = (unique[i], unique[i + 1]);
            var (fx, fy) = (Math.Clamp(f(x), 0.0, 1.0), Math.Clamp(f(y), 0.0, 1.0));
            if (fx <= 0.0 && fy <= 0.0)
                continue;
            var (bx, by) = (Boundary(x, a), Boundary(y, a));
            output.Add(TransitionLayer.TriUv(source, [c, bx, by], [c, bx, by], [(fx + fy) / 2.0, fx, fy], 1.0));
        }
        return output;
    }

    // ---------- wielokąty ----------

    /// <summary>Część wypukłego wielokąta, gdzie <c>n · p ≤ d</c>.</summary>
    public static List<UvPoint> Clip(IReadOnlyList<UvPoint> poly, UvPoint n, double d)
    {
        var output = new List<UvPoint>(poly.Count + 2);
        double Side(UvPoint p) => n.X * p.X + n.Y * p.Y - d;
        for (var i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            var (sp, sq) = (Side(p), Side(q));
            if (sp <= 0.0)
                output.Add(p);
            if ((sp < 0.0 && sq > 0.0) || (sp > 0.0 && sq < 0.0))
            {
                var k = sp / (sp - sq);
                output.Add(new UvPoint(p.X + (q.X - p.X) * k, p.Y + (q.Y - p.Y) * k));
            }
        }
        return output;
    }

    /// <summary>Wypukły wielokąt jako trójkąty: wierzchołki w <paramref name="map"/>(p), współrzędna tekstury p.</summary>
    public static List<TransitionLayer> PolyLayers(TransitionSource source, IReadOnlyList<UvPoint> poly, double alpha, Func<UvPoint, UvPoint> map)
    {
        var output = new List<TransitionLayer>();
        if (poly.Count == 0)
            return output;
        var first = poly[0];
        for (var i = 1; i + 1 < poly.Count; i++)
            output.Add(TransitionLayer.TriUv(source, [map(first), map(poly[i]), map(poly[i + 1])], [first, poly[i], poly[i + 1]], [1.0, 1.0, 1.0], alpha));
        return output;
    }

    public static List<UvPoint> RectPoly(double[] r) => [.. TransitionLayer.Corners(r)];

    // ---------- 3-D ----------

    public static V3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>Pole czworokąta ze znakiem (dodatnie: przodem do widza).</summary>
    public static double Area(UvPoint[] q)
    {
        var s = 0.0;
        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (q[i], q[(i + 1) % 4]);
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2.0;
    }

    /// <summary>
    /// Karta: <paramref name="src"/> źródła na narożnikach 3-D, jeśli jest przodem do widza; <paramref name="shade"/>
    /// (0..1) ją przyciemnia. Widziana od tyłu jest pomijana, chyba że <paramref name="back"/> (wtedy lustrzanie).
    /// </summary>
    public static List<TransitionLayer> Card(Cam cam, TransitionSource source, double[] src, V3[] c, double alpha, double shade, bool back)
    {
        var q = cam.Quad(c);
        if (!q.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
            return [];
        var front = Area(q) > 0.0;
        if (!front && !back)
            return [];
        var l = TransitionLayer.OnQuad(source, src, q, alpha);
        var v = new List<TransitionLayer> { l };
        if (shade > 0.001)
            v.Add(l.Shade(shade));
        return v;
    }

    /// <summary>Jak bardzo karta skierowana wzdłuż swojej normalnej jest przyciemniona światłem zza widza: 0 na wprost.</summary>
    public static double FacingShade(V3[] c, double k)
    {
        var u = c[1].Sub(c[0]);
        var w = c[3].Sub(c[0]);
        var n = new V3(u.Y * w.Z - u.Z * w.Y, u.Z * w.X - u.X * w.Z, u.X * w.Y - u.Y * w.X);
        var len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
        if (len < 1e-12)
            return 0.0;
        var cos = Math.Abs(n.Z / len);
        return Math.Clamp(k * (1.0 - cos), 0.0, 1.0);
    }
}
