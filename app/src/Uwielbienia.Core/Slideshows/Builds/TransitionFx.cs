// Port of DeckCraft crates/anim/src/transition_fx.rs (every transition effect).
using static Uwielbienia.Core.Slideshows.Builds.TransitionGeom;
using static Uwielbienia.Core.Slideshows.Builds.TransitionSource;

namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>
/// Efekty przejść zmierzone na PowerPoincie (<c>transition_fx.rs</c>). <c>t</c> to liniowy postęp (0..1), każdy efekt
/// nakłada własną krzywą. Kierunki jak opcja w pliku: dla Wypychania, Wycierania, Zakrywania, Odsłaniania, Panoramy,
/// Wiru, Sześcianu… <c>dir</c> to kierunek ruchu (<c>l</c>: w lewo); dla Brokatu — strona, z której efekt rusza;
/// róg Fali — dokąd fala zmierza.
/// </summary>
internal static class TransitionFx
{
    private static readonly double[] Full = TransitionLayer.FullRect;

    private static readonly string[] RandomKinds =
    [
        "fade", "push", "wipe", "split", "reveal", "randomBar", "shape", "uncover", "cover", "fallOver", "drape", "curtains", "wind",
        "prestige", "fracture", "crush", "peelOff", "pageCurlDouble", "airplane", "origami", "dissolve", "checker", "blinds", "clock",
        "ripple", "honeycomb", "glitter", "vortex", "shred", "switch", "flip", "gallery", "cube", "doors", "box", "comb", "zoom", "pan",
        "ferris", "conveyor", "rotate", "window", "orbit", "flythrough",
    ];

    private static TransitionLayer L(TransitionSource s, double alpha) => TransitionLayer.Full(s, alpha);

    private static double[] Cell(int c, int r, int n, int m) => [c / (double)n, r / (double)m, (c + 1) / (double)n, (r + 1) / (double)m];

    public static List<TransitionLayer> Layers(string kind, string o, double t, double a, uint depth)
    {
        switch (kind)
        {
            case "none":
                return [L(New, 1.0)];
            case "cut":
                return o == "throughBlack" && t < 0.5 ? [L(Black, 1.0)] : [L(New, 1.0)];
            case "fade" when o == "throughBlack":
                return ThroughBlack(t, 0.5, 0.5);
            case "fade" or "morph":
                return [L(Old, 1.0), L(New, Ss(t))];
            case "push":
            {
                var (dx, dy) = Dir(o, (-1.0, 0.0));
                var s = Q2(t);
                return [Moved(Old, dx * s, dy * s, 1.0), Moved(New, dx * (s - 1.0), dy * (s - 1.0), 1.0)];
            }
            case "uncover":
            {
                var (dx, dy) = Dir(o, (-1.0, 0.0));
                // po przekątnej stary slajd przyspiesza i znika w pierwszej trzeciej
                var s = ByteLen(o) == 2 ? EIn(Win(t, 0.05, 0.35)) : PowInOut(t, 3.0);
                return [L(New, 1.0), Moved(Old, dx * s, dy * s, 1.0)];
            }
            case "cover":
            {
                var (dx, dy) = Dir(o, (-1.0, 0.0));
                // po przekątnej nowy slajd wpada szybko i powoli osiada
                var s = ByteLen(o) == 2 ? 1.0 - Math.Pow(1.0 - Win(t, 0.07, 0.95), 3) : PowInOut(t, 4.0);
                return [L(Old, 1.0), Moved(New, dx * (s - 1.0), dy * (s - 1.0), 1.0)];
            }
            case "wipe": return Wipe(o, t);
            case "split": return Split(o, t);
            case "reveal": return Reveal(o, t);
            case "randomBar": return RandomBars(o == "vert", t);
            case "shape": return Shape(o, t, a);
            case "flash": return Flash(t);
            case "dissolve": return Dissolve(t);
            case "checker": return Checker(o == "vert", t, a);
            case "blinds": return Blinds(o == "vert", t, a);
            case "clock": return Clock(o, t, a);
            case "ripple": return Ripple(o, t, a);
            case "comb": return Comb(o == "vert", t);
            case "zoom": return Zoom(o, t, a);
            case "pan": return Pan(o, t);
            case "fallOver": return FallOver(o == "r", t, a);
            case "drape": return Drape(o == "r", t, a);
            case "curtains": return Curtains(t);
            case "wind": return Wind(o == "r", t, a);
            case "prestige": return Prestige(t, a);
            case "fracture": return Fracture(t, a);
            case "crush": return Crush(t, a);
            case "peelOff": return PeelOff(o == "r", t);
            case "pageCurlDouble": return PageCurl(o, t);
            case "airplane": return Airplane(o == "r", t);
            case "origami": return Origami(o == "r", t);
            case "honeycomb": return Honeycomb(t, a);
            case "glitter": return Glitter(o, t, a);
            case "vortex": return Vortex(o, t, a);
            case "shred": return Shred(o, t, a);
            case "switch": return Switch(o == "r", t, a);
            case "flip": return Flip(o == "r", t, a);
            case "gallery": return Gallery(o == "r", t, a);
            case "conveyor": return Conveyor(o == "r", t, a);
            case "cube" or "box" or "rotate" or "orbit": return Prism(kind, o, t, a);
            case "doors" or "window": return Doors(kind == "window", o == "horz", t, a);
            case "ferris": return Ferris(o == "r", t, a);
            case "flythrough": return Flythrough(o, t);
            case "random" when depth == 0:
            {
                var h = System.Text.Encoding.UTF8.GetBytes(o).Aggregate(0x51EDUL, (acc, b) => Hash64(acc ^ b));
                var k = RandomKinds[(int)(h % (ulong)RandomKinds.Length)];
                var opt = TransitionCatalog.Options(k).FirstOrDefault() ?? "";
                return Layers(k, opt, t, a, depth + 1);
            }
            default:
                return [L(Old, 1.0), L(New, Ss(t))];
        }
    }

    /// <summary>Stary gaśnie do czerni w [0, out], nowy wyłania się z czerni w [inn, 1] (smoothstep).</summary>
    private static List<TransitionLayer> ThroughBlack(double t, double @out, double inn)
    {
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        if (t < @out)
            v.Add(L(Old, 1.0 - Ss(Win(t, 0.0, @out))));
        if (t > inn)
            v.Add(L(New, Ss(Win(t, inn, 1.0))));
        return v;
    }

    // ---------- Subtelne ----------

    /// <summary>Szerokość miękkiej krawędzi Wycierania i Podziału (jednostki slajdu wzdłuż ruchu).</summary>
    private const double WipeSoft = 0.8;

    /// <summary>Gdzie jest środek miękkiej krawędzi w chwili t: od całkiem przed slajdem do całkiem za nim.</summary>
    private static double WipePos(double t) => -WipeSoft / 2.0 + (1.0 + WipeSoft) * 1.05 * t;

    private static List<TransitionLayer> Wipe(string o, double t)
    {
        var c = WipePos(t);
        // położenie wzdłuż ruchu, 0 tam, gdzie nowy slajd zaczyna się pokazywać
        Func<double, double, double> p = o switch
        {
            "l" => (x, _) => 1.0 - x,
            "u" => (_, y) => 1.0 - y,
            "d" => (_, y) => y,
            "lu" => (x, y) => (2.0 - x - y) / 2.0,
            "ru" => (x, y) => (1.0 + x - y) / 2.0,
            "ld" => (x, y) => (1.0 - x + y) / 2.0,
            "rd" => (x, y) => (x + y) / 2.0,
            _ => (x, _) => x,
        };
        var diag = ByteLen(o) == 2;
        var (n, m) = diag ? (32, 18) : o is "u" or "d" ? (1, 36) : (48, 1);
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        v.AddRange(Field(New, n, m, (x, y) => Edge(c, p(x, y), WipeSoft)));
        return v;
    }

    private static List<TransitionLayer> Split(string o, double t)
    {
        var horz = o.StartsWith("horz", StringComparison.Ordinal);
        var inward = o.EndsWith("In", StringComparison.Ordinal);
        var c = WipePos(t);
        double P(double x, double y)
        {
            var d = horz ? Math.Abs(y - 0.5) * 2.0 : Math.Abs(x - 0.5) * 2.0;
            return inward ? 1.0 - d : d;
        }
        var (n, m) = horz ? (1, 36) : (48, 1);
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        v.AddRange(Field(New, n, m, (x, y) => Edge(c, P(x, y), WipeSoft)));
        return v;
    }

    private static List<TransitionLayer> Reveal(string o, double t)
    {
        var dx = o.EndsWith("Right", StringComparison.Ordinal) ? 1.0 : -1.0;
        if (o.StartsWith("black", StringComparison.Ordinal))
        {
            // stary odjeżdża, gasnąc do czerni, chwila czerni, nowy wjeżdża z czerni
            var vb = new List<TransitionLayer> { L(Black, 1.0) };
            if (t < 0.4)
            {
                var u = Win(t, 0.0, 0.38);
                vb.Add(Moved(Old, dx * 0.12 * EIn(u), 0.0, 1.0 - Ss(u)));
            }
            if (t > 0.55)
            {
                var u = Win(t, 0.55, 1.0);
                vb.Add(Moved(New, -dx * 0.12 * (1.0 - EOut(u)), 0.0, Ss(u)));
            }
            return vb;
        }
        // gładko: tła się przenikają, stara zawartość odjeżdża, gasnąc, a nowa podąża za nią
        var v = new List<TransitionLayer> { L(OldBack, 1.0), L(NewBack, Ss(t)) };
        if (t < 0.5)
        {
            var u = Win(t, 0.0, 0.45);
            v.Add(Moved(OldContent, dx * 0.12 * EIn(u), 0.0, 1.0 - Ss(u)));
        }
        if (t > 0.45)
        {
            var u = Win(t, 0.47, 0.95);
            v.Add(Moved(NewContent, -dx * 0.12 * (1.0 - EOut(u)), 0.0, Ss(u)));
        }
        return v;
    }

    private static List<TransitionLayer> RandomBars(bool vert, double t)
    {
        // cienkie paski, każdy szybko przenika się we własnej, losowej chwili
        const int n = 160;
        const double w = 0.2;
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        for (var i = 0; i < n; i++)
        {
            var start = Rnd((ulong)i * 7 + 17) * (1.0 - w);
            var k = Win(t, start, start + w);
            if (k <= 0.0)
                continue;
            var (lo, hi) = (i / (double)n, (i + 1) / (double)n);
            double[] r = vert ? [lo, 0.0, hi, 1.0] : [0.0, lo, 1.0, hi];
            v.Add(TransitionLayer.InPlace(New, r, k));
        }
        return v;
    }

    private static List<TransitionLayer> Shape(string o, double t, double a)
    {
        switch (o)
        {
            // Kształt do środka: stary slajd kurczy się do środka w pierwszej połowie
            case "in":
            {
                var s = 1.0 - Win(t, 0.0, 0.5);
                var v = new List<TransitionLayer> { L(New, 1.0) };
                if (s > 0.0)
                    v.AddRange(SoftRect(Old, Full, ScaleRect(Full, s), 0.012, 1.0));
                return v;
            }
            // Kształt na zewnątrz: nowy slajd rośnie ze środka w drugiej połowie
            case "out":
            {
                var s = Win(t, 0.5, 1.0);
                var v = new List<TransitionLayer> { L(Old, 1.0) };
                if (s > 0.0)
                    v.AddRange(SoftRect(New, Full, ScaleRect(Full, s), 0.012, 1.0));
                return v;
            }
            default:
            {
                const double soft = 0.18;
                var c = -soft / 2.0 + (1.0 + soft) * t;
                Func<double, double, double> f;
                if (o == "diamond")
                    f = (x, y) => Edge(c, Math.Abs(x - 0.5) + Math.Abs(y - 0.5), soft);
                else if (o == "plus")
                    f = (x, y) => Edge(c, FMin(Math.Abs(x - 0.5), Math.Abs(y - 0.5)) * 2.0, soft);
                else
                {
                    var rmax = Math.Sqrt(Sq(0.5 * a) + 0.25);
                    f = (x, y) => Edge(c, Math.Sqrt(Sq((x - 0.5) * a) + Sq(y - 0.5)) / rmax, soft);
                }
                var v = new List<TransitionLayer> { L(Old, 1.0) };
                v.AddRange(Field(New, 48, 27, f));
                return v;
            }
        }
    }

    private static double Sq(double x) => x * x;

    private static List<TransitionLayer> Flash(double t)
    {
        // lampa błyskowa: stary szybko wypala się do bieli, biel trwa, potem schodzi z nowego slajdu
        var white = t < 0.1 ? t / 0.1 : t < 0.47 ? 1.0 : 1.0 - Win(t, 0.47, 0.88);
        var @base = t < 0.2 ? Old : New;
        return [L(@base, 1.0), L(White, white)];
    }

    // ---------- Ekscytujące: wzory ----------

    private static List<TransitionLayer> Dissolve(double t)
    {
        const int n = 48, m = 27;
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        for (var r = 0; r < m; r++)
            for (var c = 0; c < n; c++)
                if (t > Rnd((ulong)(r * n + c) + 991) * 0.97)
                    v.Add(TransitionLayer.InPlace(New, Cell(c, r, n, m), 1.0));
        return v;
    }

    /// <summary>Jedna płytka obracająca się w 3-D wokół własnej osi pionowej (albo poziomej): stara strona w pierwszej połowie <paramref name="k"/>, nowa w drugiej.</summary>
    private static void FlipTile(Cam cam, double[] cell, double k, bool verticalAxis, List<TransitionLayer> output)
    {
        if (k <= 0.0)
        {
            output.Add(TransitionLayer.InPlace(Old, cell, 1.0));
            return;
        }
        if (k >= 1.0)
        {
            output.Add(TransitionLayer.InPlace(New, cell, 1.0));
            return;
        }
        var ang = 180.0 * k;
        var cc = Center(cell);
        var c3 = cam.Unit(cc.X, cc.Y);
        var (src, deg) = k < 0.5 ? (Old, ang) : (New, ang - 180.0);
        var pts = cam.Rect(cell).Select(p => verticalAxis ? p.Yaw(c3, deg) : p.Pitch(c3, deg)).ToArray();
        var sh = FacingShade(pts, 0.6);
        output.AddRange(Card(cam, src, cell, pts, 1.0, sh, true));
    }

    private static List<TransitionLayer> Checker(bool vert, double t, double a)
    {
        const int n = 10, m = 8;
        var cam = new Cam(a, 3.0);
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        for (var r = 0; r < m; r++)
            for (var c = 0; c < n; c++)
            {
                var cell = Cell(c, r, n, m);
                var across = vert ? r / (double)m : c / (double)n;
                var start = across * 0.55 + Rnd((ulong)(r * n + c) + 5) * 0.12;
                var k = Q2(Win(t, start, start + 0.3));
                FlipTile(cam, cell, k, !vert, v);
            }
        return v;
    }

    private static List<TransitionLayer> Blinds(bool vert, double t, double a)
    {
        const int n = 12;
        var cam = new Cam(a, 3.0);
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        for (var i = 0; i < n; i++)
        {
            var (s, e) = (i / (double)n, (i + 1) / (double)n);
            double[] cell = vert ? [s, 0.0, e, 1.0] : [0.0, s, 1.0, e];
            var start = 0.1 + 0.3 * i / n;
            var k = Q2(Win(t, start, start + 0.4));
            FlipTile(cam, cell, k, vert, v);
        }
        return v;
    }

    private static List<TransitionLayer> Clock(string o, double t, double a)
    {
        // wskazówka przesuwa się ze stałą prędkością, z miękką krawędzią z przodu
        const double soft = 0.12;
        var c = -soft / 2.0 + (1.0 + soft) * t;
        double F(double ang)
        {
            var p = o switch
            {
                "counterClockwise" => (2.0 * Math.PI - ang) / (2.0 * Math.PI),
                "wedge" => (ang > Math.PI ? 2.0 * Math.PI - ang : ang) / Math.PI,
                _ => ang / (2.0 * Math.PI),
            };
            return Edge(c, p, soft);
        }
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        v.AddRange(Fan(New, a, 240, F));
        return v;
    }

    private static List<TransitionLayer> Ripple(string o, double t, double a)
    {
        // miękki krąg nowego slajdu rozchodzi się ze środka albo z rogu naprzeciw tego, dokąd zmierza fala
        var origin = o switch
        {
            "lu" => new UvPoint(1.0, 1.0),
            "ru" => new UvPoint(0.0, 1.0),
            "ld" => new UvPoint(1.0, 0.0),
            "rd" => new UvPoint(0.0, 0.0),
            _ => new UvPoint(0.5, 0.5),
        };
        var far = new[] { new UvPoint(0.0, 0.0), new UvPoint(1.0, 0.0), new UvPoint(0.0, 1.0), new UvPoint(1.0, 1.0) }
            .Select(p => Math.Sqrt(Sq((p.X - origin.X) * a) + Sq(p.Y - origin.Y)))
            .Aggregate(0.0, FMax);
        const double soft = 0.16;
        var c = -soft / 2.0 + (1.0 + soft) * t;
        double D(double x, double y) => Math.Sqrt(Sq((x - origin.X) * a) + Sq(y - origin.Y)) / FMax(far, 1e-6);
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        v.AddRange(Field(New, 48, 27, (x, y) => Edge(c, D(x, y), soft)));
        // pierścienie cienia biegnące wewnątrz fali
        v.AddRange(Field(Black, 48, 27, (x, y) =>
        {
            var d = D(x, y);
            var inside = Edge(c, d, soft);
            var w = Math.Abs(Math.Sin((d - c) * 30.0));
            return 0.18 * inside * (1.0 - w) * (1.0 - t);
        }));
        return v;
    }

    private static List<TransitionLayer> Comb(bool vert, double t)
    {
        // pasy starego slajdu rozjeżdżają się na przemian w obie strony, jeden po drugim od ostatniego
        var n = vert ? 10 : 7;
        var v = new List<TransitionLayer> { L(New, 1.0) };
        for (var i = 0; i < n; i++)
        {
            var (lo, hi) = (i / (double)n, (i + 1) / (double)n);
            var d = (n - 1 - i) % 2 == 0 ? 1.0 : -1.0;
            var start = 0.06 + 0.05 * (n - 1 - i) * 7.0 / n;
            var s = EIn(Win(t, start, start + 0.55));
            if (s >= 1.0)
                continue;
            double[] band = vert ? [lo, 0.0, hi, 1.0] : [0.0, lo, 1.0, hi];
            var (ox, oy) = vert ? (0.0, d * s) : (d * s, 0.0);
            v.Add(TransitionLayer.Rect(Old, band, Offset(band, ox, oy), 1.0));
        }
        return v;
    }

    private static List<TransitionLayer> Zoom(string o, double t, double a)
    {
        switch (o)
        {
            // Powiększenie z obrotem: nowy slajd wkręca się ze środka, pełny obrót, pojawiając się w miarę wzrostu
            case "rotate":
            {
                var u = Win(t, 0.1, 0.98);
                var s = 0.1 + 0.9 * u;
                var ang = 360.0 * (1.0 - u);
                var r = ScaleRect(Full, s);
                var q = RotQuad(TransitionLayer.Corners(r), new UvPoint(0.5, 0.5), ang, a);
                return [L(Old, 1.0), TransitionLayer.OnQuad(New, Full, q, 0.35 + 0.65 * Ss(Win(t, 0.0, 0.45)))];
            }
            // Pomniejszenie: nowy schodzi z dużego pod spodem, a stary kurczy się, gasnąc
            case "out":
            {
                var s = EOut(Win(t, 0.0, 0.8));
                return
                [
                    Scaled(New, 1.9 - 0.9 * s, 0.5, 0.5, 0.0, 0.0, 1.0),
                    Scaled(Old, 1.0 - 0.5 * EOut(t), 0.5, 0.5, 0.0, 0.0, 1.0 - Ss(Win(t, 0.0, 0.95))),
                ];
            }
            // Powiększenie: stary rośnie w stronę widza, gasnąc, nowy rośnie na miejsce z małego
            default:
                return
                [
                    L(Black, 1.0),
                    Scaled(Old, 1.0 + 0.3 * Ss(t), 0.5, 0.5, 0.0, 0.0, 1.0 - Ss(Win(t, 0.0, 0.9))),
                    Scaled(New, 0.5 + 0.5 * Ss(Win(t, 0.15, 0.95)), 0.5, 0.5, 0.0, 0.0, Ss(Win(t, 0.0, 0.85))),
                ];
        }
    }

    private static List<TransitionLayer> FallOver(bool right, double t, double a)
    {
        // stary slajd przechyla się do przodu wokół dolnej krawędzi i wypada z widoku
        var cam = new Cam(a, 2.2);
        var u = EIn(Win(t, 0.0, 0.88));
        var ang = 92.0 * u;
        var pivot = V(0.0, 0.5, 0.0);
        var side = right ? 1.0 : -1.0;
        var pts = cam.Rect(Full).Select(p => p.Pitch(pivot, ang).Roll(pivot, side * 6.0 * u)).ToArray();
        var v = new List<TransitionLayer> { L(New, 1.0) };
        if (u < 1.0)
            v.AddRange(Card(cam, Old, Full, pts, 1.0, FacingShade(pts, 0.4), true));
        return v;
    }

    private static List<TransitionLayer> Drape(bool right, double t, double a)
    {
        // nowy slajd opada od górnej krawędzi jak materiał na stary
        var cam = new Cam(a, 2.2);
        var u = Q2(Win(t, 0.25, 0.62));
        var wob = 4.0 * (1.0 - Win(t, 0.6, 1.0)) * Math.Sin(Win(t, 0.6, 1.0) * Math.PI * 2.0);
        var ang = 90.0 * (1.0 - u) + wob * u;
        var pivot = V(0.0, -0.5, 0.0);
        var side = right ? -1.0 : 1.0;
        var pts = cam.Rect(Full).Select(p => p.Pitch(pivot, -ang).Roll(pivot, side * 3.0 * (1.0 - u))).ToArray();
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        if (u > 0.0)
            v.AddRange(Card(cam, New, Full, pts, 1.0, FacingShade(pts, 0.5), true));
        return v;
    }

    private static List<TransitionLayer> Curtains(double t)
    {
        // stary slajd rozchyla się pośrodku na dwie zasłony rozsuwane na boki, potem uniesione
        var open = Q2(Win(t, 0.0, 0.35));
        var lift = EIn(Win(t, 0.62, 0.9));
        var v = new List<TransitionLayer> { L(New, 1.0) };
        var wTop = 0.5 - 0.22 * open;
        var wBot = 0.5 - 0.12 * open;
        var up = 0.15 * lift;
        var sw = 0.75 * lift;
        UvPoint[] left = [new(-sw, -up), new(wTop - sw, -up), new(wBot - 0.6 * sw, 1.0 - up), new(-0.6 * sw, 1.0 - up)];
        UvPoint[] right = [new(1.0 - wTop + sw, -up), new(1.0 + sw, -up), new(1.0 + 0.6 * sw, 1.0 - up), new(1.0 - wBot + 0.6 * sw, 1.0 - up)];
        v.Add(TransitionLayer.OnQuad(Old, [0.0, 0.0, 0.5, 1.0], left, 1.0));
        v.Add(TransitionLayer.OnQuad(Old, [0.5, 0.0, 1.0, 1.0], right, 1.0));
        return v;
    }

    private static List<TransitionLayer> Wind(bool right, double t, double a)
    {
        // stary slajd odlatuje jak kartka, unosząc się i oddalając w jedną stronę
        var cam = new Cam(a, 2.2);
        var u = EIn(Win(t, 0.12, 0.8));
        var side = right ? 1.0 : -1.0;
        var pivot = V(0.0, 0.0, 0.0);
        var pts = cam.Rect(Full).Select(p =>
        {
            var w = p.Pitch(V(0.0, -0.5, 0.0), -25.0 * u).Yaw(pivot, -side * 20.0 * u);
            return V(w.X + side * 1.6 * u * a / 2.0, w.Y - 0.8 * u, w.Z + 0.5 * u);
        }).ToArray();
        var v = new List<TransitionLayer> { L(New, 1.0) };
        if (u < 1.0)
            v.AddRange(Card(cam, Old, Full, pts, 1.0, 0.0, true));
        return v;
    }

    private static List<TransitionLayer> Prestige(double t, double a)
    {
        // stary slajd unosi się za dolną krawędź i odlatuje w górę
        var cam = new Cam(a, 2.2);
        var lift = Q2(Win(t, 0.4, 0.62));
        var fly = EIn(Win(t, 0.5, 0.85));
        var pivot = V(0.0, -0.5, 0.0);
        var pts = cam.Rect(Full).Select(p =>
        {
            var w = p.Pitch(pivot, -40.0 * lift).Roll(pivot, -12.0 * fly);
            return V(w.X - 0.4 * fly * a / 2.0, w.Y - 1.3 * fly, w.Z - 0.2 * fly);
        }).ToArray();
        var v = new List<TransitionLayer> { L(New, 1.0) };
        if (fly < 1.0)
            v.AddRange(Card(cam, Old, Full, pts, 1.0, FacingShade(pts, 0.3), true));
        return v;
    }

    private static List<TransitionLayer> Fracture(double t, double a)
    {
        // stary slajd pęka na długie odłamki rozchodzące się ze środka; odłamki lekko się rozsuwają, potem spadają, górne pierwsze
        var v = new List<TransitionLayer> { L(New, 1.0) };
        var crack = Ss(Win(t, 0.06, 0.2));
        if (crack <= 0.0)
        {
            v.Add(L(Old, 1.0));
            return v;
        }
        const int n = 36;
        double[] rings = [0.0, 0.3, 0.6, 1.0];
        var c = new UvPoint(0.5, 0.5);
        for (var i = 0; i < n; i++)
        {
            var a0 = 2.0 * Math.PI * (i + 0.35 * Rnd((ulong)i)) / n;
            var a1 = 2.0 * Math.PI * (i + 1.0 + 0.35 * Rnd((ulong)(i + 1) % n)) / n;
            for (var r = 0; r < 3; r++)
            {
                var (r0, r1) = (rings[r], r == 2 ? 1.6 : rings[r + 1]);
                UvPoint P(double ang, double rr)
                {
                    var b = Boundary(ang, a);
                    return new UvPoint(c.X + (b.X - c.X) * rr, c.Y + (b.Y - c.Y) * rr);
                }
                UvPoint[] pts = [P(a0, r0), P(a1, r0), P(a1, r1), P(a0, r1)];
                var id = (ulong)(i * 3 + r);
                var mid = new UvPoint((pts[0].X + pts[1].X + pts[2].X + pts[3].X) / 4.0, (pts[0].Y + pts[1].Y + pts[2].Y + pts[3].Y) / 4.0);
                var start = 0.3 + 0.28 * Math.Clamp(mid.Y, 0.0, 1.0) + 0.1 * Rnd(id + 300);
                var k = Win(t, start, start + 0.35);
                var gap = 0.015 * crack;
                var @out = new UvPoint((mid.X - c.X) * gap * 4.0, (mid.Y - c.Y) * gap * 4.0 + 1.4 * k * k);
                var ang = (Rnd(id + 7) - 0.5) * 50.0 * k;
                var q = pts.Select(p =>
                {
                    var pp = new UvPoint(mid.X + (p.X - mid.X) * (1.0 - 0.06 * crack) + @out.X, mid.Y + (p.Y - mid.Y) * (1.0 - 0.06 * crack) + @out.Y);
                    return Rot(pp, new UvPoint(mid.X + @out.X, mid.Y + @out.Y), ang, a);
                }).ToArray();
                var uv = pts.Select(p => new UvPoint(Math.Clamp(p.X, 0.0, 1.0), Math.Clamp(p.Y, 0.0, 1.0))).ToArray();
                if (k < 1.0)
                {
                    var l = new TransitionLayer(Old, Full, q, uv, 1.0, TransitionLayer.Ones());
                    v.Add(l);
                    v.Add(l.Shade(0.1 * crack * Rnd(id + 9) + 0.2 * k));
                }
            }
        }
        return v;
    }

    private static List<TransitionLayer> Crush(double t, double a)
    {
        // stary slajd zgniata się w kulę, która spada
        var v = new List<TransitionLayer> { L(New, 1.0) };
        var k = Q2(Win(t, 0.08, 0.55));
        var drop = EIn(Win(t, 0.65, 1.0));
        var size = 1.0 - 0.45 * k;
        var cx = 0.5 - 0.1 * k + 0.05 * drop;
        var cy = 0.5 + 0.05 * k + 0.8 * drop;
        const int n = 40;
        var c = new UvPoint(0.5, 0.5);
        UvPoint J(double ang, int idx)
        {
            var b = Boundary(ang, a);
            // narożniki ściągają się do okrągłej kuli z poszarpanymi zębami
            var (dx, dy) = ((b.X - 0.5) * a, b.Y - 0.5);
            var len = FMax(Math.Sqrt(dx * dx + dy * dy), 1e-9);
            const double rr = 0.45;
            var round = new UvPoint(0.5 + dx / len * rr / a, 0.5 + dy / len * rr);
            var jag = 1.0 - 0.18 * k * Rnd((ulong)(idx % n) + 77);
            var p = new UvPoint(b.X + (round.X - b.X) * k, b.Y + (round.Y - b.Y) * k);
            return new UvPoint(cx + (p.X - c.X) * size * jag, cy + (p.Y - c.Y) * size * jag);
        }
        for (var i = 0; i < n; i++)
        {
            var a0 = 2.0 * Math.PI * i / n;
            var a1 = 2.0 * Math.PI * (i + 1) / n;
            var (p0, p1) = (J(a0, i), J(a1, i + 1));
            var (u0, u1) = (Boundary(a0, a), Boundary(a1, a));
            var l = TransitionLayer.TriUv(Old, [new(cx, cy), p0, p1], [c, u0, u1], [1.0, 1.0, 1.0], 1.0);
            v.Add(l);
            v.Add(l.Shade(0.35 * k * Rnd((ulong)i + 13)));
        }
        return v;
    }

    private static List<TransitionLayer> PeelOff(bool right, double t)
    {
        // stary slajd odkleja się od dolnego rogu w stronę przeciwległego górnego; odklejony płat pokazuje założony tył strony
        var u = Ss(Win(t, 0.2, 0.95));
        // linia zgięcia prostopadła do przekątnej, przesuwająca się od rogu
        var (cx, cy) = right ? (0.0, 1.0) : (1.0, 1.0);
        var nd = right ? new UvPoint(1.0, -1.0) : new UvPoint(-1.0, -1.0);
        var len = Math.Sqrt(nd.X * nd.X + nd.Y * nd.Y);
        var n = new UvPoint(nd.X / len, nd.Y / len);
        var span = Math.Sqrt(2.0);
        var d0 = n.X * cx + n.Y * cy;
        var fold = d0 + span * 1.25 * u;
        var v = new List<TransitionLayer> { L(New, 1.0) };
        var full = RectPoly(Full);
        // zostająca część: za linią zgięcia, patrząc od rogu
        var keep = Clip(full, new UvPoint(-n.X, -n.Y), -fold);
        v.AddRange(PolyLayers(Old, keep, 1.0, p => p));
        // odklejona część, odbita względem linii zgięcia
        var peeled = Clip(full, n, fold);
        UvPoint Refl(UvPoint p)
        {
            var d = n.X * p.X + n.Y * p.Y - fold;
            return new UvPoint(p.X - 2.0 * d * n.X, p.Y - 2.0 * d * n.Y);
        }
        foreach (var l in PolyLayers(Old, peeled, 1.0, Refl))
        {
            v.Add(l);
            v.Add(l.Shade(0.25));
        }
        return v;
    }

    private static List<TransitionLayer> PageCurl(string o, double t)
    {
        var single = o.StartsWith("single", StringComparison.Ordinal);
        var right = o.EndsWith('r') || o.EndsWith('R');
        var v = new List<TransitionLayer> { L(New, 1.0) };
        // wałek przesuwający się po starym slajdzie
        void Roll(double[] area, double k, bool towardLeft)
        {
            var w = area[2] - area[0];
            const double r = 0.06;
            if (towardLeft)
            {
                var e = area[2] - (w + r) * k;
                double[] keep = [area[0], area[1], FMax(e, area[0]), area[3]];
                if (keep[2] > keep[0])
                    v.Add(TransitionLayer.InPlace(Old, keep, 1.0));
                // wałek: pas ściskający zwiniętą część
                double[] band = [FMax(e, area[0]), area[1], FMax(FMin(e + r, area[2]), area[0]), area[3]];
                if (band[2] > band[0] && k < 1.0)
                {
                    double[] src = [band[0], area[1], area[2], area[3]];
                    var l = TransitionLayer.Rect(Old, src, band, 1.0);
                    v.Add(l);
                    v.Add(l.Shade(0.3).Faded([0.2, 1.0, 1.0, 0.2]));
                }
            }
            else
            {
                var e = area[0] + (w + r) * k;
                double[] keep = [FMin(e, area[2]), area[1], area[2], area[3]];
                if (keep[2] > keep[0])
                    v.Add(TransitionLayer.InPlace(Old, keep, 1.0));
                double[] band = [FMin(FMax(e - r, area[0]), area[2]), area[1], FMin(e, area[2]), area[3]];
                if (band[2] > band[0] && k < 1.0)
                {
                    double[] src = [area[0], area[1], band[2], area[3]];
                    var l = TransitionLayer.Rect(Old, src, band, 1.0);
                    v.Add(l);
                    v.Add(l.Shade(0.3).Faded([1.0, 0.2, 0.2, 1.0]));
                }
            }
        }
        if (single)
        {
            var k = Q2(Win(t, 0.25, 0.85));
            Roll(Full, k, !right);
        }
        else
        {
            // podwójne: najpierw dalsza połowa, potem bliższa
            var k1 = Q2(Win(t, 0.1, 0.6));
            var k2 = Q2(Win(t, 0.45, 0.85));
            if (right)
            {
                Roll([0.0, 0.0, 0.5, 1.0], k1, false);
                Roll([0.5, 0.0, 1.0, 1.0], k2, false);
            }
            else
            {
                Roll([0.5, 0.0, 1.0, 1.0], k1, true);
                Roll([0.0, 0.0, 0.5, 1.0], k2, true);
            }
        }
        return v;
    }

    private static List<TransitionLayer> Airplane(bool right, double t)
    {
        // stary slajd składa się w duży papierowy samolot (dziobem w prawo dla „l”), który wisi nisko, potem odlatuje w górę
        var fold = Q2(Win(t, 0.0, 0.3));
        var fly = EIn(Win(t, 0.82, 0.95));
        var v = new List<TransitionLayer> { L(New, 1.0) };
        if (fly >= 1.0)
            return v;
        UvPoint Mx(UvPoint p) => right ? new UvPoint(1.0 - p.X, p.Y) : p;
        // samolot w pozie wiszącej („l”), potem odlatujący
        var drift = 0.04 * Win(t, 0.3, 0.82);
        UvPoint Pose(UvPoint p)
        {
            p = new UvPoint(p.X + drift, p.Y + drift);
            var c = new UvPoint(0.75, 0.65);
            var k = 1.0 - 0.85 * fly;
            return new UvPoint(c.X + (p.X - c.X) * k - 0.4 * fly, c.Y + (p.Y - c.Y) * k - 1.0 * fly);
        }
        UvPoint[] dart = [new(0.42, 0.28), new(1.02, 0.66), new(1.0, 0.72), new(0.46, 1.08)];
        UvPoint[] flat = [new(0.0, 0.0), new(1.0, 0.0), new(1.0, 1.0), new(0.0, 1.0)];
        var pts = Enumerable.Range(0, 4).Select(i =>
        {
            var (f, d) = (flat[i], dart[i]);
            return Mx(Pose(new UvPoint(f.X + (d.X - f.X) * fold, f.Y + (d.Y - f.Y) * fold)));
        }).ToArray();
        var uv = flat.Select(Mx).ToArray();
        var l1 = TransitionLayer.TriUv(Old, [pts[0], pts[1], pts[2]], [uv[0], uv[1], uv[2]], [1.0, 1.0, 1.0], 1.0);
        var l2 = TransitionLayer.TriUv(Old, [pts[0], pts[2], pts[3]], [uv[0], uv[2], uv[3]], [1.0, 1.0, 1.0], 1.0);
        v.Add(l1);
        v.Add(l1.Shade(0.1 * fold));
        v.Add(l2);
        v.Add(l2.Shade(0.3 * fold));
        return v;
    }

    private static List<TransitionLayer> Origami(bool right, double t)
    {
        // stary slajd składa się w ptaka, który odlatuje
        var fold = Q2(Win(t, 0.0, 0.2));
        var fly = EIn(Win(t, 0.2, 0.85));
        var side = right ? 1.0 : -1.0;
        var v = new List<TransitionLayer> { L(New, 1.0) };
        if (Win(t, 0.2, 0.92) >= 1.0)
            return v;
        var size = 1.0 - 0.35 * fold;
        var cx = 0.47 + side * 0.65 * fly;
        var cy = 0.42 + 0.05 * fly;
        var flap = Math.Sin(t * 18.0) * 0.08 * fold;
        UvPoint Body(UvPoint p) => new(cx + (p.X - 0.5) * size, cy + (p.Y - 0.5) * size);
        (UvPoint Flat, UvPoint Bird)[] shape =
        [
            (new(0.0, 0.0), new(-0.45 * side, -0.25 - flap)),
            (new(1.0, 0.0), new(0.15 * side, -0.4 + flap)),
            (new(1.0, 1.0), new(0.45 * side, 0.05)),
            (new(0.0, 1.0), new(-0.1 * side, 0.2)),
        ];
        var pts = shape.Select(s => Body(new UvPoint(s.Flat.X + (0.5 + s.Bird.X - s.Flat.X) * fold, s.Flat.Y + (0.5 + s.Bird.Y - s.Flat.Y) * fold))).ToArray();
        var uv = shape.Select(s => s.Flat).ToArray();
        var l1 = TransitionLayer.TriUv(Old, [pts[0], pts[1], pts[2]], [uv[0], uv[1], uv[2]], [1.0, 1.0, 1.0], 1.0);
        var l2 = TransitionLayer.TriUv(Old, [pts[0], pts[2], pts[3]], [uv[0], uv[2], uv[3]], [1.0, 1.0, 1.0], 1.0);
        v.Add(l1);
        v.Add(l1.Shade(0.15 * fold));
        v.Add(l2);
        v.Add(l2.Shade(0.3 * fold));
        return v;
    }

    private static List<TransitionLayer> Honeycomb(double t, double a)
    {
        // stary slajd dzieli się na sześciokątne komórki wzdłuż ciemnych szwów; komórki lecą na widza i znikają,
        // zostawiając czerń; komórki nowego pojawiają się jedna po drugiej i zamykają szwy
        const int cols = 11;
        var w = 1.0 / cols;
        var rh = w * a * 0.5;
        // rzędy co 0.866*rh; liczymy rzędy, których PŁASKA strefa wciąż kryje dół slajdu (#420)
        var rows = (int)FMax(Math.Ceiling(1.0 / (rh * 0.866) - 1.0 / 3.0), 1.0) + 1;
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        var seam = Ss(Win(t, 0.0, 0.12)) * (1.0 - Ss(Win(t, 0.85, 1.0)));
        UvPoint[] Hex(double cx, double cy, double s) => Enumerable.Range(0, 6).Select(k =>
        {
            var ang = Math.PI / 3.0 * k + Math.PI / 6.0;
            return new UvPoint(cx + Math.Cos(ang) * s * w * 0.577, cy + Math.Sin(ang) * s * rh * 0.577 / 0.5 * 0.5);
        }).ToArray();
        for (var r = 0; r < rows; r++)
            for (var c = 0; c <= cols; c++)
            {
                var cx = (c + (r % 2 == 1 ? 0.5 : 0.0)) * w;
                var cy = r * rh * 0.866;
                var id = (ulong)(r * 64 + c);
                var srcHex = Hex(cx, cy, 1.0);
                var outStart = 0.1 + 0.2 * Rnd(id + 1);
                var outK = EIn(Win(t, outStart, outStart + 0.12));
                var inStart = 0.38 + 0.42 * Rnd(id + 3);
                var inK = EOut(Win(t, inStart, inStart + 0.1));
                var gap = 1.0 - 0.1 * seam;
                void CellOf(TransitionSource src, double s, double alpha)
                {
                    var dst = Hex(cx, cy, s);
                    for (var k = 0; k < 6; k++)
                    {
                        var (p0, p1) = (dst[k], dst[(k + 1) % 6]);
                        var (u0, u1) = (srcHex[k], srcHex[(k + 1) % 6]);
                        v.Add(TransitionLayer.TriUv(src, [new(cx, cy), p0, p1], [new(cx, cy), u0, u1], [1.0, 1.0, 1.0], alpha));
                    }
                }
                if (t < 0.5 && outK < 1.0)
                    CellOf(Old, gap * (1.0 + 3.0 * outK), 1.0 - outK * outK);
                if (inK > 0.0)
                    CellOf(New, gap * (0.3 + 0.7 * inK), 1.0);
            }
        return v;
    }

    private static List<TransitionLayer> Glitter(string o, double t, double a)
    {
        // płytki obracają się ze starego slajdu na nowy w iskrzącym przejściu od strony z opcji
        var hexes = o.StartsWith("hex", StringComparison.Ordinal);
        var rest = o;
        while (rest.StartsWith("hex", StringComparison.Ordinal))
            rest = rest[3..];
        var d = string.Concat(rest.Select(ch => ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch));
        var (n, m) = hexes ? (28, 16) : (40, 22);
        var cam = new Cam(a, 3.0);
        var v = new List<TransitionLayer> { L(Old, 1.0) };
        for (var r = 0; r < m; r++)
            for (var c = 0; c < n; c++)
            {
                var cell = Cell(c, r, n, m);
                var cc = Center(cell);
                // odległość od strony, z której rusza przejście („r”: od prawej)
                var grad = d switch
                {
                    "r" => 1.0 - cc.X,
                    "u" => cc.Y,
                    "d" => 1.0 - cc.Y,
                    _ => cc.X,
                };
                var start = 0.75 * grad * 0.85 + Rnd((ulong)(r * n + c) + 77) * 0.12;
                var k = Win(t, start, start + 0.25);
                if (k <= 0.0)
                    continue;
                v.Add(TransitionLayer.InPlace(Black, cell, 1.0));
                FlipTile(cam, cell, k, true, v);
            }
        return v;
    }

    private static List<TransitionLayer> Vortex(string o, double t, double a)
    {
        // stary slajd rozpada się na drobinki wirujące w stronę z opcji, tylna strona pierwsza; drobinki nowego
        // wirują do środka i osiadają, przednia strona pierwsza
        var (dx, dy) = Dir(o, (-1.0, 0.0));
        const int n = 48, m = 27;
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        for (var r = 0; r < m; r++)
            for (var c = 0; c < n; c++)
            {
                var id = (ulong)(r * n + c);
                var cell = Cell(c, r, n, m);
                var cc = Center(cell);
                // 1 po stronie, w którą rzeczy się ruszają, 0 po drugiej
                var lead = Math.Clamp(0.5 + (cc.X - 0.5) * dx + (cc.Y - 0.5) * dy, 0.0, 1.0);
                var go = 0.4 * lead + 0.08 * Rnd(id + 31);
                var ko = Win(t, go, go + 0.35);
                var end = 0.62 + 0.3 * (1.0 - lead) + 0.06 * Rnd(id + 41);
                var kn = 1.0 - Win(t, end - 0.35, end);
                void Piece(TransitionSource src, double k)
                {
                    var swirl = 2.0 * Math.PI * (Rnd(id + 5) - 0.5) * k;
                    var off = new UvPoint(dx * k * 0.8 + Math.Sin(swirl) * 0.15 * k, dy * k * 0.8 - Math.Abs(Math.Cos(swirl)) * 0.12 * k);
                    var s = 0.8 - 0.5 * k;
                    var r2 = Offset(ScaleRect(cell, s), off.X, off.Y);
                    var q = RotQuad(TransitionLayer.Corners(r2), Center(r2), Rnd(id) * 360.0 * k, a);
                    v.Add(TransitionLayer.OnQuad(src, cell, q, 1.0 - k * k));
                }
                if (kn < 1.0)
                    Piece(New, kn);
                if (ko > 0.0 && ko < 1.0 && kn >= 1.0)
                    Piece(Old, ko);
                else if (ko <= 0.0)
                    v.Add(TransitionLayer.InPlace(Old, cell, 1.0));
            }
        return v;
    }

    private static List<TransitionLayer> Shred(string o, double t, double a)
    {
        var particles = o.StartsWith("particles", StringComparison.Ordinal) || o.StartsWith("rect", StringComparison.Ordinal);
        var @out = o.EndsWith("Out", StringComparison.Ordinal);
        var cam = new Cam(a, 2.5);
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        // pierwsza połowa: stary się rozpada i oddala; druga: nowy się składa
        var (src, k) = t < 0.55 ? (Old, Q2(Win(t, 0.05, 0.55))) : (New, 1.0 - Q2(Win(t, 0.55, 0.95)));
        if (particles)
        {
            const int n = 64, m = 36;
            for (var r = 0; r < m; r++)
                for (var c = 0; c < n; c++)
                {
                    var id = (ulong)(r * n + c);
                    var cell = Cell(c, r, n, m);
                    var cc = Center(cell);
                    var z = (@out ? -0.6 : 1.5) * k * (0.5 + Rnd(id));
                    var jitter = new UvPoint((Rnd(id + 1) - 0.5) * 0.4 * k, (Rnd(id + 2) - 0.5) * 0.4 * k);
                    var p3 = cam.Unit(cc.X + jitter.X, cc.Y + jitter.Y);
                    var pc = cam.P(V(p3.X, p3.Y, z));
                    var s = 0.8 * (1.0 - 0.3 * k) * cam.D / FMax(cam.D + z, 0.1);
                    var r2 = Offset(ScaleRect(cell, s), pc.X - cc.X, pc.Y - cc.Y);
                    var l = TransitionLayer.Rect(src, cell, r2, 1.0 - 0.3 * k * Rnd(id + 5));
                    v.Add(l);
                    v.Add(l.Shade(0.4 * k));
                }
        }
        else
        {
            const int n = 30;
            for (var i = 0; i < n; i++)
            {
                var id = (ulong)i;
                var (x0, x1) = (i / (double)n, (i + 1) / (double)n);
                double[] cell = [x0, 0.0, x1, 1.0];
                var z = (@out ? -0.3 : 1.0) * k * k * (0.6 + Rnd(id + 11));
                var tilt = (Rnd(id + 12) - 0.5) * 30.0 * k;
                var drop = (Rnd(id + 13) - 0.3) * 0.25 * k;
                var inset = 0.004 * k;
                var w3 = cam.Rect([x0 + inset, 0.0, x1 - inset, 1.0]);
                var c3 = cam.Unit((x0 + x1) / 2.0, 0.5);
                var pts = w3.Select(p =>
                {
                    var q = p.Roll(c3, tilt * 0.2).Yaw(c3, tilt);
                    return V(q.X, q.Y + drop, q.Z + z);
                }).ToArray();
                v.AddRange(Card(cam, src, cell, pts, 1.0, 0.3 * k, true));
            }
        }
        return v;
    }

    private static List<TransitionLayer> Switch(bool right, double t, double a)
    {
        // zamiana kart: stary wysuwa się w jedną stronę („l”: w lewo) i chowa z tyłu, a nowy, wysunięty spod niego
        // w drugą stronę, wychodzi na przód; obie lekko odwracają się zewnętrznymi brzegami
        var cam = new Cam(a, 2.5);
        var side = right ? -1.0 : 1.0;
        var u = (t + Ss(t)) / 2.0;
        var k = Math.Sin(u * Math.PI);
        var shift = 0.5 * k * a;
        var old = cam.Rect(Full).Select(p =>
        {
            var q = p.Yaw(V(0.0, 0.0, 0.0), side * 14.0 * k);
            return V(q.X - side * shift, q.Y, q.Z + 0.12 * k);
        }).ToArray();
        var @new = cam.Rect(Full).Select(p =>
        {
            var q = p.Yaw(V(0.0, 0.0, 0.0), -side * 14.0 * k);
            return V(q.X + side * shift, q.Y, q.Z + 0.12 * k);
        }).ToArray();
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        var o = Card(cam, Old, Full, old, 1.0, FacingShade(old, 0.3), true);
        var n = Card(cam, New, Full, @new, 1.0, FacingShade(@new, 0.3), true);
        if (u < 0.5)
        {
            v.AddRange(n);
            v.AddRange(o);
        }
        else
        {
            v.AddRange(o);
            v.AddRange(n);
        }
        return v;
    }

    private static List<TransitionLayer> Flip(bool right, double t, double a)
    {
        // slajd odwraca się jak karta: stara strona, potem nowa na jej odwrocie
        var cam = new Cam(a, 2.5);
        var s = Q2(t);
        var side = right ? 1.0 : -1.0;
        var deg = 180.0 * s * side;
        var back = 0.6 * Math.Sin(s * Math.PI);
        var c = V(0.0, 0.0, 0.0);
        var pts = cam.Rect(Full).Select(p =>
        {
            var q = p.Yaw(c, deg);
            return V(q.X, q.Y, q.Z + back);
        }).ToArray();
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        if (s < 0.5)
            v.AddRange(Card(cam, Old, Full, pts, 1.0, FacingShade(pts, 0.5), true));
        else
        {
            // odwrót: nowy slajd odbity, żeby czytał się prawidłowo
            V3[] m = [pts[1], pts[0], pts[3], pts[2]];
            v.AddRange(Card(cam, New, Full, m, 1.0, FacingShade(m, 0.5), true));
        }
        return v;
    }

    private static List<TransitionLayer> Gallery(bool right, double t, double a)
    {
        // slajdy wiszą na ścianie: ściana odwraca się i oddala, slajdy jadą wzdłuż niej, potem wraca; pod spodem słabe odbicie
        var cam = new Cam(a, 2.5);
        var side = right ? 1.0 : -1.0;
        var k = Ss(Win(t, 0.0, 0.35)) * (1.0 - Ss(Win(t, 0.7, 1.0)));
        var s = Q2(Win(t, 0.3, 0.8));
        const double gap = 0.15;
        var back = 0.7 * k;
        var yaw = -side * 22.0 * k;
        var v = new List<TransitionLayer> { L(Black, 1.0) };
        foreach (var (src, shift) in new[] { (Old, side * s), (New, side * (s - 1.0)) })
        {
            var x0 = shift * (a + gap);
            var pts = cam.Rect(Full).Select(p => V(p.X + x0, p.Y, p.Z + back)).Select(p => p.Yaw(V(0.0, 0.0, back), yaw)).ToArray();
            // odbicie: slajd odwrócony pod dolną krawędzią, słaby
            V3[] refl = [pts[3], pts[2], V(pts[2].X, pts[2].Y + 0.25, pts[2].Z), V(pts[3].X, pts[3].Y + 0.25, pts[3].Z)];
            var rq = cam.Quad(refl);
            v.Add(new TransitionLayer(src, [0.0, 0.75, 1.0, 1.0], rq, [new(0.0, 1.0), new(1.0, 1.0), new(1.0, 0.75), new(0.0, 0.75)], 0.3 * k,
                [1.0, 1.0, 0.0, 0.0]));
            v.AddRange(Card(cam, src, Full, pts, 1.0, 0.0, true));
        }
        return v;
    }

    private static List<TransitionLayer> Conveyor(bool right, double t, double a)
    {
        // zawartość jedzie na taśmie: taśma odwraca się, zawartość przesuwa, taśma wraca; tła przenikają się pod spodem
        var cam = new Cam(a, 2.5);
        var side = right ? 1.0 : -1.0;
        var k = Ss(Win(t, 0.0, 0.35)) * (1.0 - Ss(Win(t, 0.65, 1.0)));
        var s = Q2(Win(t, 0.33, 0.75));
        var back = 0.45 * k;
        var yaw = -side * 18.0 * k;
        var v = new List<TransitionLayer> { L(OldBack, 1.0), L(NewBack, Ss(t)) };
        foreach (var (src, shift) in new[] { (OldContent, side * s), (NewContent, side * (s - 1.0)) })
        {
            var x0 = shift * a * 1.05;
            var pts = cam.Rect(Full).Select(p => V(p.X + x0, p.Y, p.Z + back)).Select(p => p.Yaw(V(0.0, 0.0, back), yaw)).ToArray();
            v.AddRange(Card(cam, src, Full, pts, 1.0, 0.0, true));
        }
        return v;
    }

    private static List<TransitionLayer> Prism(string kind, string o, double t, double a)
    {
        // Sześcian: slajdy to dwie ściany obracającego się sześcianu; Pudełko: dwie ściany wewnątrz pudełka;
        // Obrót i Orbita: to samo z samą zawartością, nad przenikającymi się tłami
        var (dx, dy) = Dir(o, (-1.0, 0.0));
        var vertical = dy != 0.0 && dx == 0.0;
        var content = kind is "rotate" or "orbit";
        var inverted = kind is "box" or "orbit";
        var cam = new Cam(a, 2.5);
        var s = Q2(t);
        var half = vertical ? 0.5 : a / 2.0;
        // ściany sześcianu wokół (0, 0, half) (z zewnątrz) albo wnętrze pudełka wokół (0, 0, -half)
        var c = V(0.0, 0.0, inverted ? -half : half);
        // dodatni odwraca prawą (albo górną) stronę od widza
        var sign = vertical ? -dy : dx;
        var deg = 90.0 * s * sign * (inverted ? -1.0 : 1.0);
        V3 Turn(V3 p, double @base) => vertical ? p.Pitch(c, @base + deg) : p.Yaw(c, @base + deg);
        var sideBase = -90.0 * sign * (inverted ? -1.0 : 1.0);
        var (os, ns) = content ? (OldContent, NewContent) : (Old, New);
        var old = cam.Rect(Full).Select(p => Turn(p, 0.0)).ToArray();
        var @new = cam.Rect(Full).Select(p => Turn(p, sideBase)).ToArray();
        // najbliższa krawędź zostaje na płaszczyźnie ekranu, jak w kamerze PowerPointa
        var near = old.Concat(@new).Select(p => p.Z).Aggregate(double.PositiveInfinity, FMin);
        var lift = inverted ? 0.0 : -FMin(near, 0.0) - FMax(near, 0.0);
        // pudełko w połowie obrotu widać z dalszej odległości
        var back = (inverted ? 0.5 : 0.15) * Math.Sin(s * Math.PI);
        V3[] Push(V3[] q) => q.Select(p => V(p.X, p.Y, p.Z + lift + back)).ToArray();
        (old, @new) = (Push(old), Push(@new));
        var v = content ? new List<TransitionLayer> { L(OldBack, 1.0), L(NewBack, Ss(t)) } : new List<TransitionLayer> { L(Black, 1.0) };
        v.AddRange(Card(cam, os, Full, old, 1.0, content ? 0.0 : FacingShade(old, 0.6), false));
        v.AddRange(Card(cam, ns, Full, @new, 1.0, content ? 0.0 : FacingShade(@new, 0.6), false));
        return v;
    }

    private static List<TransitionLayer> Doors(bool window, bool horz, double t, double a)
    {
        // stary slajd rozchyla się na dwie połowy, które otwierają się w stronę widza i ciemnieją; nowy wychodzi ze
        // środka, pojawiając się nad nimi. Okno robi to z samą zawartością, nad przenikającymi się tłami
        var cam = new Cam(a, 2.2);
        var open = EIn(Win(t, 0.05, 0.68));
        var come = Q2(Win(t, 0.0, 0.72));
        var (os, ns) = window ? (OldContent, NewContent) : (Old, New);
        var v = window ? new List<TransitionLayer> { L(OldBack, 1.0), L(NewBack, Ss(t)) } : new List<TransitionLayer> { L(Black, 1.0) };
        (double[] R, double Sgn)[] halves = horz
            ? [([0.0, 0.0, 1.0, 0.5], -1.0), ([0.0, 0.5, 1.0, 1.0], 1.0)]
            : [([0.0, 0.0, 0.5, 1.0], -1.0), ([0.5, 0.0, 1.0, 1.0], 1.0)];
        if (open < 1.0)
        {
            foreach (var (r, sgn) in halves)
            {
                var hinge = horz ? cam.Unit(0.5, sgn < 0.0 ? 0.0 : 1.0) : cam.Unit(sgn < 0.0 ? 0.0 : 1.0, 0.5);
                var ang = 30.0 * open;
                var slide = 0.55 * open;
                var pts = cam.Rect(r).Select(p =>
                {
                    var q = horz ? p.Pitch(hinge, sgn * ang) : p.Yaw(hinge, -sgn * ang);
                    return horz ? V(q.X, q.Y + sgn * slide, q.Z) : V(q.X + sgn * slide * a, q.Y, q.Z);
                }).ToArray();
                var dark = window ? 0.0 : 0.7 * open;
                var alpha = window ? 1.0 - Ss(Win(t, 0.2, 0.6)) : 1.0;
                v.AddRange(Card(cam, os, r, pts, alpha, dark, true));
            }
        }
        var sc = 0.5 + 0.5 * come;
        var fadeIn = window ? Ss(t) : Ss(Win(t, 0.05, 0.62));
        v.Add(Scaled(ns, sc, 0.5, 0.5, 0.0, 0.0, fadeIn));
        return v;
    }

    private static List<TransitionLayer> Ferris(bool right, double t, double a)
    {
        // zawartość jedzie na dużym kole obracającym się wokół osi daleko za dolnym rogiem: stara opada w dół
        // i w bok, nowa zjeżdża łukiem z góry, nad przenikającymi się tłami
        var side = right ? -1.0 : 1.0;
        var v = new List<TransitionLayer> { L(OldBack, 1.0), L(NewBack, Ss(t)) };
        var @out = EIn(Win(t, 0.0, 0.5));
        if (@out < 1.0)
        {
            var (dx, dy) = (-side * 0.45 * Math.Pow(@out, 1.5), 0.9 * @out);
            var q = RotQuad(TransitionLayer.Corners(Offset(Full, dx, dy)), new UvPoint(0.5 + dx, 0.5 + dy), side * -15.0 * @out, a);
            v.Add(TransitionLayer.OnQuad(OldContent, Full, q, 1.0));
        }
        var inn = Q2(Win(t, 0.5, 1.0));
        if (inn > 0.0)
        {
            var k = 1.0 - inn;
            var (dx, dy) = (-side * 0.5 * Math.Pow(k, 1.3), -0.75 * k);
            var r = Offset(ScaleRect(Full, 1.0 - 0.25 * k), dx, dy);
            var q = RotQuad(TransitionLayer.Corners(r), Center(r), side * -18.0 * k, a);
            v.Add(TransitionLayer.OnQuad(NewContent, Full, q, 1.0));
        }
        return v;
    }

    private static List<TransitionLayer> Pan(string o, double t)
    {
        // zawartości jadą razem, a tła się przenikają
        var (dx, dy) = Dir(o, (-1.0, 0.0));
        // szybki start, długi poślizg
        var s = Ease(t, 0.2, 0.8, 0.0);
        return
        [
            L(OldBack, 1.0),
            L(NewBack, s),
            Moved(OldContent, dx * s, dy * s, 1.0),
            Moved(NewContent, dx * (s - 1.0), dy * (s - 1.0), 1.0),
        ];
    }

    private static List<TransitionLayer> Flythrough(string o, double t)
    {
        // zawartości przelatują obok widza (in) albo się oddalają (out), a tła się przenikają
        var bounce = o.EndsWith("Bounce", StringComparison.Ordinal);
        var @out = o.StartsWith("out", StringComparison.Ordinal);
        var s = Q2(t);
        var b = bounce ? 0.08 * Math.Sin(Win(t, 0.6, 1.0) * Math.PI) : 0.0;
        var oldA = 1.0 - Ss(Win(t, 0.0, 0.6));
        var newA = Ss(Win(t, 0.3, 0.95));
        var (os, ns) = @out ? (1.0 - 0.5 * s, 1.8 - 0.8 * s - b) : (1.0 + 1.5 * s, 0.4 + 0.6 * s + b);
        return
        [
            L(OldBack, 1.0),
            L(NewBack, Ss(t)),
            Scaled(NewContent, ns, 0.5, 0.5, 0.0, 0.0, newA),
            Scaled(OldContent, os, 0.5, 0.5, 0.0, 0.0, oldA),
        ];
    }
}
