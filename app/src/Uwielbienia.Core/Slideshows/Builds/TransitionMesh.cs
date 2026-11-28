// Port of DeckCraft crates/anim/src/mesh.rs (MeshVertex, layer_mesh).
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Wierzchołek siatki warstwy przejścia.</summary>
/// <param name="Position">Położenie w jednostkach slajdu (0..1, y w dół).</param>
/// <param name="Uv">Współrzędna tekstury (0..1).</param>
/// <param name="Opacity">Krycie: krycie warstwy razy krycie narożników, interpolowane.</param>
public readonly record struct MeshVertex(UvPoint Position, UvPoint Uv, double Opacity);

/// <summary>
/// Siatki trójkątów warstw przejścia (<c>mesh.rs</c>). Warstwa to teksturowany czworokąt; narysowany jako dwa trójkąty
/// trapez (slajd w perspektywie) wygina teksturę wzdłuż przekątnej, bo karta graficzna interpoluje współrzędne tekstury
/// liniowo na ekranie. Taki czworokąt dzielimy na siatkę, której wierzchołki leżą na odwzorowaniu rzutowym prostokąta
/// źródła na czworokąt — obraz wygląda jak płaska karta obrócona w 3-D. Prostokąty i równoległoboki zostają dwoma
/// trójkątami, kliny — jednym.
/// </summary>
public static class TransitionMesh
{
    /// <summary>Komórki siatki na bok dla czworokątów wymagających podziału rzutowego.</summary>
    public const int Grid = 8;

    /// <summary>Wierzchołki siatki warstwy (trójkąty — <see cref="LayerMesh(TransitionLayer, out IReadOnlyList{int})"/>).</summary>
    public static IReadOnlyList<MeshVertex> LayerMesh(TransitionLayer layer) => LayerMesh(layer, out _);

    /// <summary>
    /// Trójkąty warstwy (<c>layer_mesh</c>): wierzchołki i <paramref name="triangles"/> — indeksy wierzchołków, po trzy
    /// na trójkąt (w Ruście <c>[u32; 3]</c>).
    /// </summary>
    public static IReadOnlyList<MeshVertex> LayerMesh(TransitionLayer layer, out IReadOnlyList<int> triangles)
    {
        var q = layer.Quad;
        var uv = layer.Uv;
        var fa = layer.Fade.Select(f => f * layer.Alpha).ToArray();
        // klin (trójkąt): 4. narożnik powtarza 3.
        if (q[3] == q[2] && uv[3] == uv[2])
        {
            triangles = [0, 1, 2];
            return Enumerable.Range(0, 3).Select(k => new MeshVertex(q[k], uv[k], fa[k])).ToList();
        }
        // równoległobok: przeciwległe boki równe
        var (e1x, e1y) = (q[1].X - q[0].X, q[1].Y - q[0].Y);
        var (e2x, e2y) = (q[2].X - q[3].X, q[2].Y - q[3].Y);
        var affine = Math.Abs(e1x - e2x) < 1e-9 && Math.Abs(e1y - e2y) < 1e-9;
        var h = affine ? null : SquareToQuad(q);
        if (h is null)
        {
            triangles = [0, 1, 2, 0, 2, 3];
            return Enumerable.Range(0, 4).Select(k => new MeshVertex(q[k], uv[k], fa[k])).ToList();
        }
        // małe czworokąty (płytki, odłamki) wyginają teksturę zbyt słabo, żeby to było widać: mniej komórek
        var span = q.SelectMany(p => q.Select(r => TransitionGeom.FMax(Math.Abs(p.X - r.X), Math.Abs(p.Y - r.Y)))).Aggregate(0.0, TransitionGeom.FMax);
        var n = Math.Clamp(ToUsize(Math.Ceiling(span * 2.0 * Grid)), 1, Grid);
        var v = new List<MeshVertex>((n + 1) * (n + 1));
        for (var j = 0; j <= n; j++)
        {
            var b = j / (double)n;
            for (var i = 0; i <= n; i++)
            {
                var a = i / (double)n;
                // tekstura: dwuliniowo w (a, b) po czterech narożnikach uv (dokładnie dla prostokątów)
                var top = Lerp(uv[0], uv[1], a);
                var bot = Lerp(uv[3], uv[2], a);
                var t = Lerp(top, bot, b);
                var at = fa[0] + (fa[1] - fa[0]) * a;
                var ab = fa[3] + (fa[2] - fa[3]) * a;
                v.Add(new MeshVertex(Apply(h, a, b), t, at + (ab - at) * b));
            }
        }
        var tris = new List<int>(n * n * 6);
        var w = n + 1;
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var p = j * w + i;
                tris.AddRange([p, p + 1, p + w + 1]);
                tris.AddRange([p, p + w + 1, p + w]);
            }
        triangles = tris;
        return v;
    }

    /// <summary><c>f64 as usize</c>: NaN i liczby ujemne → 0, za duże — nasycenie.</summary>
    private static int ToUsize(double x) => double.IsNaN(x) || x <= 0 ? 0 : x >= int.MaxValue ? int.MaxValue : (int)x;

    private static UvPoint Lerp(UvPoint a, UvPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>
    /// Odwzorowanie rzutowe narożników kwadratu jednostkowego (0,0), (1,0), (1,1), (0,1) na tl, tr, br, bl czworokąta
    /// (Heckbert, square-to-quad). <c>null</c>, gdy czworokąt jest zdegenerowany.
    /// </summary>
    private static double[]? SquareToQuad(UvPoint[] q)
    {
        var (x0, y0, x1, y1, x2, y2, x3, y3) = (q[0].X, q[0].Y, q[1].X, q[1].Y, q[2].X, q[2].Y, q[3].X, q[3].Y);
        var sx = x0 - x1 + x2 - x3;
        var sy = y0 - y1 + y2 - y3;
        var (dx1, dx2, dy1, dy2) = (x1 - x2, x3 - x2, y1 - y2, y3 - y2);
        var den = dx1 * dy2 - dx2 * dy1;
        if (!double.IsFinite(den) || Math.Abs(den) < 1e-12)
            return null;
        var g = (sx * dy2 - dx2 * sy) / den;
        var h = (dx1 * sy - sx * dy1) / den;
        double[] m = [x1 - x0 + g * x1, x3 - x0 + h * x3, x0, y1 - y0 + g * y1, y3 - y0 + h * y3, y0, g, h];
        return m.All(double.IsFinite) ? m : null;
    }

    private static UvPoint Apply(double[] m, double u, double v)
    {
        var w = m[6] * u + m[7] * v + 1.0;
        if (Math.Abs(w) < 1e-9)
            w = Math.CopySign(1e-9, w);
        return new UvPoint((m[0] * u + m[1] * v + m[2]) / w, (m[3] * u + m[4] * v + m[5]) / w);
    }
}
