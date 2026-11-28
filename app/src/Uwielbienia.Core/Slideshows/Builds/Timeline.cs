// Port DeckCraft: crates/anim/src/timeline.rs â€” patrz UPSTREAM.md.
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>KsztaĹ‚t slajdu dla osi czasu: pole w punktach (<c>null</c> = pole domyĹ›lne na Ĺ›rodku), kolor jednolitego wypeĹ‚nienia (<c>null</c> = accent1) i akapity tekstu.</summary>
public sealed record TimelineShape(int Id, Xfrm? Box, Rgba? Fill, IReadOnlyList<ParagraphInfo>? Paragraphs);

/// <summary>
/// Czas jednego efektu (albo efektu jednego akapitu): <see cref="Anim"/> = indeks animacji, <see cref="Trigger"/> =
/// ksztaĹ‚t-wyzwalacz (<c>null</c> = gĹ‚Ăłwna sekwencja), <see cref="Step"/> = klikniÄ™cie w swojej sekwencji,
/// <see cref="Start"/> = poczÄ…tek w kroku (s, z opĂłĹşnieniem), <see cref="Duration"/> = jedno powtĂłrzenie (s),
/// <see cref="Span"/> = od poczÄ…tku do koĹ„ca (wszystkie powtĂłrzenia i powroty; jedno powtĂłrzenie, gdy do klikniÄ™cia).
/// </summary>
public sealed record EffectTiming(int Anim, int Shape, int? Paragraph, int? Trigger, int Step, double Start,
    double Duration, double Iterations, bool UntilNextClick, bool AutoReverse, bool Rewind, double Span, AnimClass Class);

/// <summary>Stan sekwencji wyzwalanej: <see cref="Step"/> klikniÄ™Ä‡ w wyzwalacz odegranych, bieĹĽÄ…ce trwa <see cref="T"/> s.</summary>
public readonly record struct TriggerPlay(int Trigger, int Step, double T);

/// <summary>Kiedy rozpoczÄ™to krok sekwencji wyzwalanej (liczone w rozpoczÄ™tych krokach na slajdzie).</summary>
public readonly record struct StepStamp(int Trigger, int Step, ulong At);

/// <summary>Animacje slajdu rozĹ‚oĹĽone w czasie: kroki (klikniÄ™cia), czasy efektĂłw i stan ksztaĹ‚tĂłw w dowolnej chwili.</summary>
public sealed class Timeline
{
    /// <summary>OdstÄ™p miÄ™dzy akapitami budowania akapitami, ktĂłre nie idzie po klikniÄ™ciu (s).</summary>
    public const double ParagraphStagger = 0.5;

    private const int MaxEffects = 100_000;
    private const double MaxIterations = 1.0e6;

    /// <summary>Kolor zastÄ™pczy, gdy ksztaĹ‚t nie ma koloru bazowego (jak w Rust).</summary>
    private static readonly Rgba FallbackColor = Rgba.Rgb(0x15, 0x60, 0x82);

    private sealed record StepInfo(bool Auto)
    {
        public double Duration { get; set; }
    }

    private abstract record AfterAction
    {
        public sealed record None : AfterAction;
        public sealed record Hide : AfterAction;
        public sealed record HideOnNextClick : AfterAction;
        public sealed record Dim(Rgba Color) : AfterAction;
    }

    private sealed record Effect(EffectTiming Timing, EffectSpec Spec, AfterAction After);

    private readonly List<Effect> _effects = [];
    private List<StepInfo> _main = [];
    private readonly List<(int Shape, List<StepInfo> Steps)> _triggers = [];
    private readonly List<(int Shape, Rgba Color)> _colors = [];
    // budowanie akapitami animuje akapity pierwszego poziomu; gĹ‚Ä™bsze idÄ… za swoim: (ksztaĹ‚t, akapit, wĹ‚aĹ›ciciel)
    private readonly List<(int Shape, int Member, int Owner)> _groups = [];

    private readonly Dictionary<(int Shape, int? Paragraph), List<int>> _byTarget = [];
    private readonly Dictionary<int, List<int>> _byShape = [];
    private readonly Dictionary<(int Shape, int Member), int> _owner = [];
    private readonly Dictionary<int, List<(int Member, int Owner)>> _groupsByShape = [];
    private readonly Dictionary<int, Rgba> _color = [];

    private Timeline()
    {
    }

    private static double Secs(uint ms) => ms / 1000.0;

    private static double FiniteOr(double v, double d) => double.IsFinite(v) ? v : d;

    private static Xfrm SanitizeXfrm(Xfrm x, double sw, double sh) => new(
        Math.Clamp(FiniteOr(x.X, 0.0), -1.0e7, 1.0e7),
        Math.Clamp(FiniteOr(x.Y, 0.0), -1.0e7, 1.0e7),
        Math.Clamp(FiniteOr(x.W, sw / 2.0), 0.0, 1.0e7),
        Math.Clamp(FiniteOr(x.H, sh / 2.0), 0.0, 1.0e7));

    /// <summary>
    /// Buduje oĹ› czasu (<c>Timeline::new</c>): animacje slajdu, jego ksztaĹ‚ty i rozmiar w punktach. KsztaĹ‚t bez pola dostaje
    /// pole domyĹ›lne na Ĺ›rodku, bez jednolitego wypeĹ‚nienia â€” accent1 domyĹ›lnego schematu.
    /// </summary>
    public static Timeline Create(IReadOnlyList<Animation> animations, IReadOnlyList<TimelineShape> shapes, double slideWidth, double slideHeight)
    {
        var sw = Math.Clamp(FiniteOr(slideWidth, 960.0), 1.0, 1.0e7);
        var sh = Math.Clamp(FiniteOr(slideHeight, 540.0), 1.0, 1.0e7);
        var accent = Animation.DefaultAccent;
        var tl = new Timeline();
        if (animations.Count == 0)
            return tl;
        var shapeById = new Dictionary<int, TimelineShape>();
        foreach (var s in shapes)
            shapeById.TryAdd(s.Id, s);
        var paraCache = new Dictionary<int, List<int?>>();
        // sekwencje w kolejnoĹ›ci pierwszego wystÄ…pienia (gĹ‚Ăłwna pierwsza)
        var seqs = new List<(int? Trigger, List<int> Members)> { (null, []) };
        var seqIndex = new Dictionary<int, int>();
        for (var ai = 0; ai < animations.Count; ai++)
        {
            if (animations[ai].Trigger is not { } tr)
            {
                seqs[0].Members.Add(ai);
                continue;
            }
            if (!seqIndex.TryGetValue(tr, out var i))
            {
                seqs.Add((tr, []));
                i = seqs.Count - 1;
                seqIndex[tr] = i;
            }
            seqs[i].Members.Add(ai);
        }
        var grouped = new HashSet<(int, int, int)>();
        foreach (var (seq, members) in seqs)
        {
            if (tl._effects.Count >= MaxEffects)
                break;
            var steps = new List<StepInfo>();
            var groupBegin = 0.0;
            var maxEnd = 0.0;
            foreach (var ai in members)
            {
                var a = animations[ai];
                if (tl._effects.Count >= MaxEffects)
                    break;
                var shape = shapeById.GetValueOrDefault(a.Shape);
                List<int?> paras;
                if (a.Paragraph is { } fixedPara)
                    paras = [fixedPara];
                else if (a.TextBuild == TextBuild.ByParagraph)
                {
                    if (!paraCache.TryGetValue(a.Shape, out var cached))
                    {
                        cached = [];
                        if (shape?.Paragraphs is { } text)
                        {
                            var roles = ParagraphBuild.Roles(text);
                            for (var i = 0; i < roles.Count; i++)
                            {
                                switch (roles[i])
                                {
                                    case BuildRole.Top:
                                        cached.Add(i);
                                        break;
                                    case BuildRole.Member m:
                                        if (grouped.Add((a.Shape, i, m.Owner)))
                                            tl._groups.Add((a.Shape, i, m.Owner));
                                        break;
                                }
                            }
                        }
                        if (cached.Count == 0)
                            cached.Add(null);
                        paraCache[a.Shape] = cached;
                    }
                    paras = cached;
                }
                else
                    paras = [null];
                var xf = SanitizeXfrm(shape?.Box ?? new Xfrm(sw / 4.0, sh / 4.0, sw / 2.0, sh / 2.0), sw, sh);
                var spec = BuildSpec(a, xf, sw, sh);
                var dur = Secs(a.DurationMs);
                var delay = Secs(a.DelayMs);
                var untilNextClick = a.Repeat == uint.MaxValue;
                var iterations = untilNextClick || a.Repeat <= 1 ? 1.0 : Math.Min(a.Repeat, MaxIterations);
                var span = dur * (a.AutoReverse ? 2.0 : 1.0) * iterations;
                AfterAction after = a.After switch
                {
                    "hide" => new AfterAction.Hide(),
                    "hideOnNextClick" => new AfterAction.HideOnNextClick(),
                    { } c when Rgba.FromHex(c) is { } dim => new AfterAction.Dim(dim),
                    _ => new AfterAction.None(),
                };
                var firstStart = 0.0;
                for (var k = 0; k < paras.Count; k++)
                {
                    var mode = k == 0 ? a.Start : a.Start == AnimStart.OnClick ? AnimStart.OnClick : AnimStart.WithPrevious;
                    var firstOfSeq = steps.Count == 0;
                    if (firstOfSeq && seq is not null)
                        mode = AnimStart.OnClick;
                    switch (mode)
                    {
                        case AnimStart.OnClick:
                            steps.Add(new StepInfo(false));
                            groupBegin = 0.0;
                            maxEnd = 0.0;
                            break;
                        case AnimStart.WithPrevious:
                            if (firstOfSeq)
                                steps.Add(new StepInfo(true));
                            break;
                        case AnimStart.AfterPrevious:
                            if (firstOfSeq)
                                steps.Add(new StepInfo(true));
                            groupBegin = maxEnd;
                            break;
                    }
                    var start = k > 0 && a.Start != AnimStart.OnClick ? firstStart + k * ParagraphStagger : groupBegin + delay;
                    if (k == 0)
                        firstStart = start;
                    var end = start + span;
                    maxEnd = Rs.Max(maxEnd, end);
                    var step = Math.Max(steps.Count - 1, 0);
                    var last = steps[^1];
                    // multimedia grajÄ… same: wstrzymujÄ… efekt â€žpo poprzednimâ€ť, ale nie nastÄ™pne klikniÄ™cie (#116)
                    last.Duration = Rs.Max(last.Duration, a.Class == AnimClass.Media ? start : end);
                    tl._effects.Add(new Effect(
                        new EffectTiming(ai, a.Shape, paras[k], seq, step, start, dur, iterations, untilNextClick, a.AutoReverse, a.Rewind,
                            span, a.Class),
                        spec,
                        after));
                }
            }
            if (seq is { } trigger)
                tl._triggers.Add((trigger, steps));
            else
                tl._main = steps;
        }
        // kolory bazowe animowanych ksztaĹ‚tĂłw
        var colored = new HashSet<int>();
        foreach (var e in tl._effects)
        {
            var id = e.Timing.Shape;
            if (!colored.Add(id))
                continue;
            tl._colors.Add((id, shapeById.GetValueOrDefault(id)?.Fill ?? accent));
        }
        tl.BuildIndex();
        return tl;
    }

    private void BuildIndex()
    {
        for (var i = 0; i < _effects.Count; i++)
        {
            var t = _effects[i].Timing;
            Bucket(_byTarget, (t.Shape, t.Paragraph)).Add(i);
            Bucket(_byShape, t.Shape).Add(i);
        }
        foreach (var (shape, member, owner) in _groups)
        {
            _owner.TryAdd((shape, member), owner);
            Bucket(_groupsByShape, shape).Add((member, owner));
        }
        foreach (var (shape, c) in _colors)
            _color.TryAdd(shape, c);
    }

    private static List<TV> Bucket<TK, TV>(Dictionary<TK, List<TV>> map, TK key) where TK : notnull
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        return list;
    }

    private static EffectSpec BuildSpec(Animation a, Xfrm xf, double sw, double sh)
    {
        var effect = a.Class == AnimClass.Exit ? AnimationEffects.ExitBase(a.Effect) : a.Effect;
        var path = a.Class == AnimClass.Path ? AnimationEffects.MotionPathFor(a.Effect, a.Option, a.Path) : null;
        return new EffectSpec(
            a.Class,
            effect,
            // â€žSink Downâ€ť to â€žRise Upâ€ť od koĹ„ca, wychodzi w dĂłĹ‚
            a.Effect == "sinkDown" ? "d" : a.Option,
            a.SmoothStart,
            a.SmoothEnd,
            a.BounceEnd,
            a.Amount,
            a.Color,
            path,
            xf,
            sw,
            sh);
    }

    /// <summary>Kroki gĹ‚Ăłwnej sekwencji (klikniÄ™cia i automatyczny pierwszy krok, gdy pierwszy efekt idzie z poprzednim / po poprzednim).</summary>
    public int Steps => _main.Count;

    /// <summary>Czy krok <paramref name="i"/> gĹ‚Ăłwnej sekwencji rusza sam (bez klikniÄ™cia), gdy slajd siÄ™ pojawia.</summary>
    public bool StepIsAuto(int i) => i >= 0 && i < _main.Count && _main[i].Auto;

    /// <summary>Czas kroku <paramref name="i"/> gĹ‚Ăłwnej sekwencji w sekundach (0 poza zakresem).</summary>
    public double StepDuration(int i) => i >= 0 && i < _main.Count ? _main[i].Duration : 0.0;

    /// <summary>Efekty multimediĂłw kroku <paramref name="step"/>, ktĂłre ruszajÄ… po <paramref name="from"/> i najpĂłĹşniej w <paramref name="to"/> s.</summary>
    public IReadOnlyList<EffectTiming> MediaCues(int step, double from, double to) =>
        _effects.Select(e => e.Timing)
            .Where(e => e.Class == AnimClass.Media && e.Trigger is null && e.Step == step && e.Start > from && e.Start <= to)
            .OrderBy(e => e.Start)
            .ToList();

    /// <summary>Czas caĹ‚ej gĹ‚Ăłwnej sekwencji bez przerw.</summary>
    public double TotalDuration => _main.Sum(s => s.Duration);

    /// <summary>Czas na zegarze â€žodtwĂłrz wszystkoâ€ť â†’ (krok, czas w kroku) dla <see cref="State"/>.</summary>
    public (int Step, double T) Locate(double time)
    {
        var t = Math.Max(FiniteOr(time, 0.0), 0.0);
        for (var i = 0; i < _main.Count; i++)
        {
            var s = _main[i];
            if (t < s.Duration || (t == 0.0 && s.Duration == 0.0 && i == 0))
                return (i, t);
            t -= s.Duration;
        }
        return (_main.Count, 0.0);
    }

    /// <summary>PoczÄ…tek animacji <paramref name="anim"/> na zegarze â€žodtwĂłrz wszystkoâ€ť; <c>null</c> dla wyzwalanej albo nieobecnej.</summary>
    public double? StartOf(int anim)
    {
        var e = _effects.Select(x => x.Timing).Where(x => x.Anim == anim && x.Trigger is null)
            .OrderBy(x => x.Step).ThenBy(x => x.Start).FirstOrDefault();
        if (e is null)
            return null;
        return _main.Take(e.Step).Sum(s => s.Duration) + e.Start;
    }

    /// <summary>KsztaĹ‚ty, ktĂłrych klikniÄ™cie uruchamia sekwencjÄ™.</summary>
    public IReadOnlyList<int> TriggerShapes() => _triggers.Select(t => t.Shape).ToList();

    /// <summary>Kroki (klikniÄ™cia w wyzwalacz) sekwencji wyzwalanej.</summary>
    public int TriggerSteps(int trigger) => _triggers.FirstOrDefault(t => t.Shape == trigger).Steps?.Count ?? 0;

    public double TriggerStepDuration(int trigger, int i) =>
        _triggers.FirstOrDefault(t => t.Shape == trigger).Steps is { } v && i >= 0 && i < v.Count ? v[i].Duration : 0.0;

    /// <summary>Czasy wszystkich efektĂłw w kolejnoĹ›ci sekwencji.</summary>
    public IReadOnlyList<EffectTiming> Effects => _effects.Select(e => e.Timing).ToList();

    /// <summary>Czy na slajdzie cokolwiek jest animowane.</summary>
    public bool IsEmpty => _effects.Count == 0;

    /// <summary>Czy w kroku <paramref name="step"/> gĹ‚Ăłwnej sekwencji jakiĹ› efekt powtarza siÄ™ do nastÄ™pnego klikniÄ™cia (trzeba dalej animowaÄ‡).</summary>
    public bool LoopsInStep(int step) => _effects.Any(e => e.Timing.Trigger is null && e.Timing.Step == step && e.Timing.UntilNextClick);

    /// <summary>Akapity ksztaĹ‚tu <paramref name="shape"/> animowane osobno (budowanie akapitami).</summary>
    public IReadOnlyList<int> ParagraphTargets(int shape)
    {
        var mine = _byShape.GetValueOrDefault(shape) ?? [];
        var v = mine.Select(i => _effects[i].Timing.Paragraph).OfType<int>().ToList();
        var owners = v.ToHashSet();
        if (_groupsByShape.TryGetValue(shape, out var groups))
            v.AddRange(groups.Where(g => owners.Contains(g.Owner)).Select(g => g.Member));
        return v.Distinct().Order().ToList();
    }

    /// <summary>KsztaĹ‚ty z jakimkolwiek efektem (w kolejnoĹ›ci pierwszego efektu).</summary>
    public IReadOnlyList<int> AnimatedShapes() => _effects.Select(e => e.Timing.Shape).Distinct().ToList();

    /// <summary>
    /// Stan ksztaĹ‚tu <paramref name="shape"/> (albo jego akapitu <paramref name="paragraph"/> â€” wzglÄ™dem stanu ksztaĹ‚tu, do
    /// zĹ‚oĹĽenia), gdy <paramref name="step"/> krokĂłw gĹ‚Ăłwnej sekwencji odegrano w caĹ‚oĹ›ci, a krok <paramref name="step"/> trwa
    /// <paramref name="t"/> s (<c>t &lt;= 0</c>: nie ruszyĹ‚; <c>step &gt;= Steps</c>: wszystko skoĹ„czone). Sekwencje wyzwalane â€” nieodegrane.
    /// </summary>
    public AnimState State(int shape, int? paragraph, int step, double t) => StateOrdered(shape, paragraph, step, t, [], []);

    /// <summary><see cref="State"/> ze stanem sekwencji wyzwalanych.</summary>
    public AnimState StateWith(int shape, int? paragraph, int step, double t, IReadOnlyList<TriggerPlay> triggers) =>
        StateOrdered(shape, paragraph, step, t, triggers, []);

    /// <summary><see cref="StateWith"/> z efektami sekwencji wyzwalanych w kolejnoĹ›ci rozpoczÄ™cia ich krokĂłw (<paramref name="stamps"/>).</summary>
    public AnimState StateOrdered(int shape, int? paragraph, int step, double t, IReadOnlyList<TriggerPlay> triggers, IReadOnlyList<StepStamp> stamps)
    {
        int? para = paragraph is { } p ? _owner.GetValueOrDefault((shape, p), p) : null;
        var baseColor = _color.TryGetValue(shape, out var bc) ? bc : FallbackColor;
        var st = AnimState.Identity;
        // poczÄ…tkowa widocznoĹ›Ä‡ wg pierwszego efektu w kolejnoĹ›ci okienka animacji
        IEnumerable<Effect> ordered = (_byTarget.GetValueOrDefault((shape, para)) ?? []).Select(i => _effects[i]).OrderBy(e => e.Timing.Anim).ToList();
        if (ordered.FirstOrDefault() is { Timing.Class: AnimClass.Entrance })
            st = st with { Visible = false };
        // potem w kolejnoĹ›ci czasu: gĹ‚Ăłwna sekwencja, potem wyzwalane wg klikniÄ™Ä‡
        if (stamps.Count > 0)
        {
            ulong When(Effect e)
            {
                if (e.Timing.Trigger is not { } tr)
                    return 0;
                foreach (var s in stamps)
                    if (s.Trigger == tr && s.Step == e.Timing.Step)
                        return s.At == ulong.MaxValue ? ulong.MaxValue : s.At + 1;
                return ulong.MaxValue;
            }
            ordered = ordered.OrderBy(When).ThenBy(e => e.Timing.Anim).ToList();
        }
        foreach (var e in ordered)
        {
            var (pstep, pt) = (step, t);
            if (e.Timing.Trigger is { } tr)
            {
                (pstep, pt) = (0, 0.0);
                foreach (var play in triggers)
                    if (play.Trigger == tr)
                    {
                        (pstep, pt) = (play.Step, play.T);
                        break;
                    }
            }
            var cur = st.Tint ?? baseColor;
            var revert = e.Timing.Rewind || e.Timing.AutoReverse;
            var (status, progress) = Status(e.Timing, pstep, pt);
            switch (status)
            {
                case StatusKind.NotStarted:
                    break;
                case StatusKind.Active:
                    if (e.Timing.Class is AnimClass.Entrance or AnimClass.Exit)
                        st = st with { Visible = true };
                    st = st.Compose(e.Spec.Eval(progress, cur));
                    break;
                case StatusKind.Done:
                    switch (e.Timing.Class)
                    {
                        case AnimClass.Entrance:
                            st = st with { Visible = !revert };
                            break;
                        case AnimClass.Exit:
                            st = st with { Visible = revert };
                            break;
                        case AnimClass.Emphasis or AnimClass.Path when !revert:
                            st = st.Compose(e.Spec.EndState(cur));
                            break;
                    }
                    switch (e.After)
                    {
                        case AfterAction.Hide:
                            st = st with { Visible = false };
                            break;
                        case AfterAction.HideOnNextClick when pstep > e.Timing.Step:
                            st = st with { Visible = false };
                            break;
                        case AfterAction.Dim dim when pstep > e.Timing.Step:
                            st = st with { Tint = dim.Color };
                            break;
                    }
                    break;
            }
        }
        return st.Sanitized();
    }

    /// <summary>Stan na zegarze â€žodtwĂłrz wszystkoâ€ť (podglÄ…d).</summary>
    public AnimState StateAt(int shape, int? paragraph, double time)
    {
        var (s, t) = Locate(time);
        return State(shape, paragraph, s, t);
    }

    private enum StatusKind { NotStarted, Active, Done }

    private static (StatusKind Kind, double Progress) Status(EffectTiming e, int step, double t)
    {
        if (e.Step < step)
            return (StatusKind.Done, 0.0);
        if (e.Step > step)
            return (StatusKind.NotStarted, 0.0);
        t = FiniteOr(t, 0.0);
        if (t <= 0.0)
            return (StatusKind.NotStarted, 0.0);
        var local = t - e.Start;
        if (local < 0.0)
            return (StatusKind.NotStarted, 0.0);
        var dur = e.Duration;
        if (double.IsNaN(dur) || dur <= 0.0 || !double.IsFinite(dur))
            return (StatusKind.Done, 0.0);
        if (!e.UntilNextClick && local >= e.Span)
            return (StatusKind.Done, 0.0);
        var period = e.AutoReverse ? 2.0 * dur : dur;
        var c = local % period;
        if (!double.IsFinite(c))
            c = 0.0;
        var p = c > dur ? 2.0 - c / dur : c / dur;
        return (StatusKind.Active, Math.Clamp(p, 0.0, 1.0));
    }
}
