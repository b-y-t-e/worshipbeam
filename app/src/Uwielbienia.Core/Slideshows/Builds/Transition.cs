// Port of DeckCraft crates/anim/src/transition.rs (Source, Layer, transition_frame, uses_slide_parts, DEFAULT_ASPECT).
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>
/// Co pokazuje warstwa przejścia slajdów (<c>Source</c> w DeckCraft): stary albo nowy slajd, kolor, albo — przy
/// przejściach „Zawartość dynamiczna” i Odkrywaniu — samo tło lub sama zawartość slajdu.
/// </summary>
public enum TransitionSource
{
    /// <summary>Slajd, który schodzi.</summary>
    Old,
    /// <summary>Slajd, który wchodzi.</summary>
    New,
    /// <summary>Czerń.</summary>
    Black,
    /// <summary>Biel.</summary>
    White,
    /// <summary>Samo tło starego slajdu (wypełnienie tła oraz grafika wzorca i układu).</summary>
    OldBack,
    /// <summary>Samo tło nowego slajdu.</summary>
    NewBack,
    /// <summary>Same kształty starego slajdu, na przezroczystym.</summary>
    OldContent,
    /// <summary>Same kształty nowego slajdu, na przezroczystym.</summary>
    NewContent,
}

/// <summary>Pomocnicze operacje na <see cref="TransitionSource"/> (<c>impl Source</c>).</summary>
public static class TransitionSources
{
    /// <summary>
    /// Źródło do narysowania zamiast części, gdy slajdów nie da się narysować osobno jako tła i zawartości: tło
    /// staje się całym slajdem, zawartość — niczym (<c>null</c>).
    /// </summary>
    public static TransitionSource? Whole(this TransitionSource s) => s switch
    {
        TransitionSource.OldBack => TransitionSource.Old,
        TransitionSource.NewBack => TransitionSource.New,
        TransitionSource.OldContent or TransitionSource.NewContent => null,
        _ => s,
    };

    /// <summary>Czy źródło wymaga slajdu narysowanego w częściach (tło / zawartość)?</summary>
    public static bool IsPart(this TransitionSource s) =>
        s is TransitionSource.OldBack or TransitionSource.NewBack or TransitionSource.OldContent or TransitionSource.NewContent;

    /// <summary>
    /// Stary slajd zamieniony z nowym (tło i zawartość też); czerń i biel bez zmian. Przejście odtwarzane wstecz
    /// (powrót do poprzedniego slajdu) — patrz <see cref="SlideTransitionFrames.ReverseFrame"/>.
    /// </summary>
    public static TransitionSource SwapOldNew(this TransitionSource s) => s switch
    {
        TransitionSource.Old => TransitionSource.New,
        TransitionSource.New => TransitionSource.Old,
        TransitionSource.OldBack => TransitionSource.NewBack,
        TransitionSource.NewBack => TransitionSource.OldBack,
        TransitionSource.OldContent => TransitionSource.NewContent,
        TransitionSource.NewContent => TransitionSource.OldContent,
        _ => s,
    };
}

/// <summary>Punkt w jednostkach slajdu (0..1, y w dół) albo współrzędna tekstury (0..1).</summary>
public readonly record struct UvPoint(double X, double Y);

/// <summary>
/// Jeden teksturowany czworokąt klatki przejścia (<c>Layer</c>). Klatkę rysuje się na czerni, warstwa po warstwie.
/// </summary>
/// <param name="Source">Co pokazuje warstwa.</param>
/// <param name="Src">Prostokąt źródła w przestrzeni tekstury (0..1): l, t, r, b — granice <paramref name="Uv"/>.</param>
/// <param name="Quad">Narożniki docelowe w jednostkach slajdu: tl, tr, br, bl. Trójkąt (klin) powtarza 3. narożnik jako 4.</param>
/// <param name="Uv">Współrzędna tekstury każdego narożnika (tl, tr, br, bl) — rysujemy według nich.</param>
/// <param name="Alpha">Krycie całej warstwy.</param>
/// <param name="Fade">Krycie narożników (tl, tr, br, bl), mnożone przez <paramref name="Alpha"/> i interpolowane po czworokącie.</param>
public sealed record TransitionLayer(TransitionSource Source, double[] Src, UvPoint[] Quad, UvPoint[] Uv, double Alpha, double[] Fade)
{
    internal static readonly double[] FullRect = [0.0, 0.0, 1.0, 1.0];

    internal static double[] Ones() => [1.0, 1.0, 1.0, 1.0];

    /// <summary>Narożniki prostokąta l, t, r, b: tl, tr, br, bl (<c>corners</c>).</summary>
    internal static UvPoint[] Corners(double[] r) => [new(r[0], r[1]), new(r[2], r[1]), new(r[2], r[3]), new(r[0], r[3])];

    /// <summary>Całe źródło na cały slajd.</summary>
    internal static TransitionLayer Full(TransitionSource source, double alpha) => Rect(source, FullRect, FullRect, alpha);

    /// <summary>Prostokąt <paramref name="src"/> źródła narysowany w prostokącie <paramref name="dst"/>.</summary>
    internal static TransitionLayer Rect(TransitionSource source, double[] src, double[] dst, double alpha) =>
        new(source, src, Corners(dst), Corners(src), alpha, Ones());

    /// <summary>Prostokąt <paramref name="src"/> źródła narysowany na czworokącie.</summary>
    internal static TransitionLayer OnQuad(TransitionSource source, double[] src, UvPoint[] quad, double alpha) =>
        new(source, src, quad, Corners(src), alpha, Ones());

    /// <summary>Część źródła pod <paramref name="dst"/> narysowana w miejscu.</summary>
    internal static TransitionLayer InPlace(TransitionSource source, double[] dst, double alpha) => Rect(source, dst, dst, alpha);

    /// <summary>Trójkąt z własnymi współrzędnymi tekstury i kryciem narożników.</summary>
    internal static TransitionLayer TriUv(TransitionSource source, UvPoint[] p, UvPoint[] uv, double[] fade, double alpha)
    {
        double[] xs = [uv[0].X, uv[1].X, uv[2].X];
        double[] ys = [uv[0].Y, uv[1].Y, uv[2].Y];
        return new(source, [TransitionGeom.Min(xs), TransitionGeom.Min(ys), TransitionGeom.Max(xs), TransitionGeom.Max(ys)],
            [p[0], p[1], p[2], p[2]], [uv[0], uv[1], uv[2], uv[2]], alpha, [fade[0], fade[1], fade[2], fade[2]]);
    }

    /// <summary>Ta sama warstwa z kryciem narożników.</summary>
    internal TransitionLayer Faded(double[] fade) => this with { Fade = fade };

    /// <summary>Czarna kopia kształtu tej warstwy przy <paramref name="k"/> jej krycia: cieniowanie karty.</summary>
    internal TransitionLayer Shade(double k) => this with { Source = TransitionSource.Black, Alpha = Alpha * Math.Clamp(k, 0.0, 1.0) };

    internal TransitionLayer Sanitized()
    {
        static double F(double v) => double.IsFinite(v) ? Math.Clamp(v, -1.0e6, 1.0e6) : 0.0;
        return new(Source,
            Src.Select(v => Math.Clamp(F(v), 0.0, 1.0)).ToArray(),
            Quad.Select(c => new UvPoint(F(c.X), F(c.Y))).ToArray(),
            Uv.Select(c => new UvPoint(Math.Clamp(F(c.X), 0.0, 1.0), Math.Clamp(F(c.Y), 0.0, 1.0))).ToArray(),
            TransitionGeom.ClampF(Alpha, 0.0, 1.0, 0.0),
            Fade.Select(a => TransitionGeom.ClampF(a, 0.0, 1.0, 0.0)).ToArray());
    }
}

/// <summary>
/// Klatki przejść slajdów jako warstwy teksturowanych czworokątów (<c>transition.rs</c>). Klatkę rysuje się na
/// czerni: każda warstwa odwzorowuje część obrazu źródła na czworokąt w jednostkach slajdu (0..1, y w dół), z kryciem
/// w narożnikach. Perspektywę dają rzutowane karty 3-D (<see cref="TransitionMesh.LayerMesh(TransitionLayer)"/> trzyma
/// ich tekstury prosto), miękkie brzegi — rampy krycia narożników, kliny — trójkąty (4. narożnik powtarza 3.).
/// Ruchy, krzywe, kierunki i znaczenia opcji DeckCraft zmierzył klatka po klatce na eksporcie wideo PowerPointa.
/// </summary>
public static class SlideTransitionFrames
{
    /// <summary>Proporcje slajdu przyjmowane domyślnie (16:9).</summary>
    public const double DefaultAspect = 16.0 / 9.0;

    /// <summary>
    /// Warstwy przejścia <paramref name="kind"/> (identyfikator z <see cref="TransitionCatalog.Kinds"/>) z opcją
    /// <paramref name="option"/> przy liniowym postępie <paramref name="t"/> (0..1; efekt sam nakłada swoją krzywą) na
    /// slajdzie o szerokości <paramref name="aspect"/> na jednostkę wysokości. Rysować po kolei na czerni. Przy
    /// <c>t ≥ 1</c> sam nowy slajd, przy <c>t ≤ 0</c> (albo NaN) sam stary. Morph daje zwykłe przenikanie. Warstwy ze
    /// źródłami <see cref="TransitionSources.IsPart"/> wymagają slajdów narysowanych jako tło i zawartość
    /// (<see cref="UsesSlideParts"/>).
    /// </summary>
    public static IReadOnlyList<TransitionLayer> Frame(string kind, string option, double t, double aspect)
    {
        t = TransitionGeom.ClampF(t, 0.0, 1.0, 0.0);
        aspect = double.IsFinite(aspect) && aspect > 0.01 ? Math.Min(aspect, 100.0) : DefaultAspect;
        var v = t >= 1.0 ? [TransitionLayer.Full(TransitionSource.New, 1.0)]
            : t <= 0.0 ? [TransitionLayer.Full(TransitionSource.Old, 1.0)]
            : TransitionFx.Layers(kind, option, t, aspect, 0);
        return v.Select(l => l.Sanitized()).Where(l => l.Alpha > 0.0 && l.Fade.Any(a => a > 0.0)).ToList();
    }

    /// <summary>
    /// Klatka przejścia odtwarzanego wstecz — przy powrocie do poprzedniego slajdu DeckCraft (jak PowerPoint, #1531)
    /// odtwarza przejście slajdu, z którego się cofamy, od końca: <c>t</c> biegnie od 1 do 0, a stary i nowy slajd
    /// zamieniają się rolami (<c>show/ui.rs: transition_layers(.., reverse: true, ..)</c>). Tu
    /// <paramref name="t"/> to postęp powrotu (0 = jeszcze slajd, który opuszczamy, 1 = slajd, do którego wracamy);
    /// <see cref="TransitionSource.Old"/> to slajd opuszczany, <see cref="TransitionSource.New"/> — ten, do którego
    /// wracamy. Morph DeckCraft przy powrocie nie odwraca (gra go zwykłym przenikaniem w przód).
    /// </summary>
    public static IReadOnlyList<TransitionLayer> ReverseFrame(string kind, string option, double t, double aspect) =>
        Frame(kind, option, 1.0 - t, aspect).Select(l => l with { Source = l.Source.SwapOldNew() }).ToList();

    /// <summary>
    /// Czy <paramref name="kind"/> animuje osobno tła i zawartość slajdów (przejścia „Zawartość dynamiczna”
    /// PowerPointa i Odkrywanie)? Wtedy klatka używa <see cref="TransitionSource.OldBack"/> i pokrewnych.
    /// </summary>
    public static bool UsesSlideParts(string kind, string option) => kind switch
    {
        "pan" or "ferris" or "conveyor" or "rotate" or "window" or "orbit" or "flythrough" => true,
        "reveal" => !option.StartsWith("black", StringComparison.Ordinal),
        _ => false,
    };
}
