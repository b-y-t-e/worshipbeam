using System.Text.Json;
using Uwielbienia.Core.Slideshows.Builds;

namespace Uwielbienia.Core.Tests;

/// <summary>
/// Port animacji DeckCraft (<c>Slideshows/Builds</c>) daje te same wyniki co oryginał w Rust: wartości zapisane przez
/// <c>tools/deckcraft-anim-fixtures</c> w <c>Fixtures/DeckCraftAnim</c> (odtworzenie: <c>python tools/deckcraft_sync.py</c>,
/// patrz <c>UPSTREAM.md</c>).
/// </summary>
public sealed class DeckCraftAnimTests
{
    private const double Tolerance = 1e-6;

    /// <summary>
    /// Efekty z galerii DeckCraft, których port celowo nie liczy osobno (wpadają w ogólną gałąź). Nowy efekt w oryginale
    /// zatrzyma <see cref="Every_gallery_effect_is_ported_or_listed_as_unsupported"/> — przenieść go albo dopisać tutaj.
    /// </summary>
    private static readonly HashSet<string> Unsupported = [];

    private static readonly Lazy<JsonDocument> EffectsDoc = new(() => Load("effects.json"));
    private static readonly Lazy<JsonDocument> TimelinesDoc = new(() => Load("timelines.json"));

    private static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DeckCraftAnim", name)));

    private static JsonElement EffectsRoot => EffectsDoc.Value.RootElement;

    private static IEnumerable<JsonElement> Scenarios => TimelinesDoc.Value.RootElement.GetProperty("scenarios").EnumerateArray();

    public static TheoryData<string, string, string> EffectCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var c in EffectsRoot.GetProperty("cases").EnumerateArray())
            data.Add(c.GetProperty("effect").GetString()!, c.GetProperty("class").GetString()!, c.GetProperty("option").GetString()!);
        return data;
    }

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var s in Scenarios)
            data.Add(s.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(EffectCases))]
    public void Single_effect_states_match_rust(string effect, string cls, string option)
    {
        var c = EffectsRoot.GetProperty("cases").EnumerateArray().First(x =>
            x.GetProperty("effect").GetString() == effect && x.GetProperty("class").GetString() == cls
            && x.GetProperty("option").GetString() == option);
        var shape = ShapeFrom(EffectsRoot.GetProperty("shape"));
        var slide = EffectsRoot.GetProperty("slide");
        var anim = Animation.Default(shape.Id) with
        {
            Class = ClassFrom(cls),
            Effect = effect,
            Option = option,
            DurationMs = EffectsRoot.GetProperty("durationMs").GetUInt32(),
        };
        var tl = Timeline.Create([anim], [shape], slide[0].GetDouble(), slide[1].GetDouble());
        var errors = new List<string>();
        foreach (var s in c.GetProperty("samples").EnumerateArray())
        {
            var step = s.GetProperty("step").GetInt32();
            var t = s.GetProperty("t").GetDouble();
            CompareState(s.GetProperty("state"), tl.State(shape.Id, null, step, t), $"step {step} t {t}", errors);
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Timeline_matches_rust(string name)
    {
        var sc = Scenarios.First(s => s.GetProperty("name").GetString() == name);
        var shapes = sc.GetProperty("shapes").EnumerateArray().Select(ShapeFrom).ToList();
        var animations = sc.GetProperty("animations").EnumerateArray().Select(Animation.FromJson).ToList();
        var slide = sc.GetProperty("slide");
        var tl = Timeline.Create(animations, shapes, slide[0].GetDouble(), slide[1].GetDouble());
        var errors = new List<string>();
        void Check(bool ok, string what)
        {
            if (!ok)
                errors.Add(what);
        }

        var steps = sc.GetProperty("steps").GetInt32();
        Check(tl.Steps == steps, $"steps {tl.Steps} != {steps}");
        var auto = sc.GetProperty("stepIsAuto").EnumerateArray().Select(x => x.GetBoolean()).ToList();
        var durations = sc.GetProperty("stepDuration").EnumerateArray().Select(x => x.GetDouble()).ToList();
        for (var i = 0; i < auto.Count; i++)
        {
            Check(tl.StepIsAuto(i) == auto[i], $"stepIsAuto({i})");
            Check(Near(tl.StepDuration(i), durations[i]), $"stepDuration({i}) {tl.StepDuration(i)} != {durations[i]}");
        }
        Check(Near(tl.TotalDuration, sc.GetProperty("totalDuration").GetDouble()), "totalDuration");
        Check(tl.IsEmpty == sc.GetProperty("isEmpty").GetBoolean(), "isEmpty");
        Check(tl.AnimatedShapes().SequenceEqual(Ints(sc.GetProperty("animatedShapes"))), "animatedShapes");
        Check(tl.TriggerShapes().SequenceEqual(Ints(sc.GetProperty("triggerShapes"))), "triggerShapes");
        foreach (var p in sc.GetProperty("paragraphTargets").EnumerateArray())
        {
            var id = p.GetProperty("shape").GetInt32();
            Check(tl.ParagraphTargets(id).SequenceEqual(Ints(p.GetProperty("paragraphs"))), $"paragraphTargets({id})");
        }
        foreach (var l in sc.GetProperty("locate").EnumerateArray())
        {
            var (step, t) = tl.Locate(l.GetProperty("time").GetDouble());
            Check(step == l.GetProperty("step").GetInt32() && Near(t, l.GetProperty("t").GetDouble()), $"locate({l.GetProperty("time")})");
        }

        var expected = sc.GetProperty("effects").EnumerateArray().ToList();
        Check(tl.Effects.Count == expected.Count, $"effects count {tl.Effects.Count} != {expected.Count}");
        foreach (var (e, a) in expected.Zip(tl.Effects))
        {
            var want = new EffectTiming(
                e.GetProperty("anim").GetInt32(),
                e.GetProperty("shape").GetInt32(),
                OptInt(e.GetProperty("paragraph")),
                OptInt(e.GetProperty("trigger")),
                e.GetProperty("step").GetInt32(),
                e.GetProperty("start").GetDouble(),
                e.GetProperty("duration").GetDouble(),
                e.GetProperty("iterations").GetDouble(),
                e.GetProperty("untilNextClick").GetBoolean(),
                e.GetProperty("autoReverse").GetBoolean(),
                e.GetProperty("rewind").GetBoolean(),
                e.GetProperty("span").GetDouble(),
                ClassFrom(e.GetProperty("class").GetString()!));
            Check(Near(a.Start, want.Start) && Near(a.Duration, want.Duration) && Near(a.Iterations, want.Iterations) && Near(a.Span, want.Span)
                && a with { Start = 0, Duration = 0, Iterations = 0, Span = 0 } == want with { Start = 0, Duration = 0, Iterations = 0, Span = 0 },
                $"effect {a} != {want}");
        }

        foreach (var s in sc.GetProperty("states").EnumerateArray())
        {
            var (shape, para, step, t) = Target(s);
            CompareState(s.GetProperty("state"), tl.State(shape, para, step, t), $"state({shape}, {para}, {step}, {t})", errors);
        }
        foreach (var s in sc.GetProperty("triggerStates").EnumerateArray())
        {
            var (shape, para, step, t) = Target(s);
            var play = new TriggerPlay(s.GetProperty("trigger").GetInt32(), s.GetProperty("triggerStep").GetInt32(), s.GetProperty("triggerT").GetDouble());
            CompareState(s.GetProperty("state"), tl.StateWith(shape, para, step, t, [play]), $"stateWith({play}; {shape}, {para}, {step}, {t})", errors);
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
    }

    [Fact]
    public void Every_gallery_effect_is_ported_or_listed_as_unsupported()
    {
        var missing = EffectsRoot.GetProperty("effectIds").EnumerateArray()
            .Select(e => (Id: e.GetProperty("id").GetString()!, Class: ClassFrom(e.GetProperty("class").GetString()!)))
            .Where(e => !Unsupported.Contains(e.Id) && !(AnimationEffects.Known.Contains(e.Id) && AnimationEffects.IsKnown(e.Id, e.Class)))
            .Select(e => $"{e.Id} ({e.Class})")
            .ToList();
        Assert.True(missing.Count == 0, "Nowe efekty w DeckCraft (przenieść albo dopisać do Unsupported): " + string.Join(", ", missing));
    }

    [Fact]
    public void Fixtures_come_from_the_rev_in_upstream_md()
    {
        var rev = EffectsRoot.GetProperty("rev").GetString();
        Assert.Equal(rev, TimelinesDoc.Value.RootElement.GetProperty("rev").GetString());
        var upstream = FindUpstreamMd();
        if (upstream is not null)
            // wzorce z przeniesionej wersji (nie z „Ostatnio sprawdzone”)
            Assert.Contains(rev!, File.ReadLines(upstream).First(l => l.StartsWith("- **Wersja:**", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Colours_parse_like_rust()
    {
        Assert.Equal(Rgba.Rgb(255, 0, 0), Rgba.FromHex("#FF0000"));
        Assert.Equal(Rgba.Rgb(255, 0, 0), Rgba.FromHex("FF0000"));
        Assert.Equal(new Rgba(255, 0, 0, 128), Rgba.FromHex("#FF000080"));
        Assert.Equal(Rgba.Rgb(255, 255, 255), Rgba.FromHex("fff"));
        Assert.Null(Rgba.FromHex("##FF0000"));
        Assert.Null(Rgba.FromHex("+12345"));
        Assert.Null(Rgba.FromHex("none"));
        Assert.Null(Rgba.FromHex(""));
        Assert.Null(Rgba.FromHex(null));
        Assert.Equal("#156082", Rgba.Rgb(0x15, 0x60, 0x82).ToHex());
        Assert.Equal("#15608280", new Rgba(0x15, 0x60, 0x82, 0x80).ToHex());
        // zaokrąglenie połówek od zera, jak f64::round
        Assert.Equal(Rgba.Rgb(128, 128, 128), Rgba.Rgb(0, 0, 0).Lerp(Rgba.Rgb(255, 255, 255), 0.5));
    }

    [Fact]
    public void Blank_and_deeper_paragraphs_follow_the_top_one_before_them()
    {
        var roles = ParagraphBuild.Roles([new(1, true), new(0, true), new(0, false), new(1, true), new(0, true)]);
        Assert.Equal<BuildRole>([new BuildRole.Unowned(), new BuildRole.Top(), new BuildRole.Member(1), new BuildRole.Member(1), new BuildRole.Top()], roles);
    }

    [Fact]
    public void Animation_from_cli_json_has_rust_defaults()
    {
        using var doc = JsonDocument.Parse("""{"shape": 7, "effect": "fly", "repeat": 4294967295, "color": "#00FF00"}""");
        var a = Animation.FromJson(doc.RootElement);
        Assert.Equal(Animation.Default(7) with { Effect = "fly", Repeat = uint.MaxValue, Color = Rgba.Rgb(0, 255, 0) }, a);
    }

    [Fact]
    public void Loops_in_step_marks_until_next_click_effects()
    {
        var tl = Timeline.Create(
            [Animation.Default(1) with { Effect = "pulse", Class = AnimClass.Emphasis, Repeat = uint.MaxValue }, Animation.Default(1)],
            [new TimelineShape(1, null, null, null)], 960, 540);
        Assert.True(tl.LoopsInStep(0));
        Assert.False(tl.LoopsInStep(1));
    }

    private static string? FindUpstreamMd()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var f = Path.Combine(dir.FullName, "src", "Uwielbienia.Core", "Slideshows", "Builds", "UPSTREAM.md");
            if (File.Exists(f))
                return f;
        }
        return null;
    }

    private static (int Shape, int? Paragraph, int Step, double T) Target(JsonElement s) =>
        (s.GetProperty("shape").GetInt32(), OptInt(s.GetProperty("paragraph")), s.GetProperty("step").GetInt32(), s.GetProperty("t").GetDouble());

    private static TimelineShape ShapeFrom(JsonElement s)
    {
        var box = s.GetProperty("box");
        var paras = s.GetProperty("paragraphs");
        return new TimelineShape(
            s.GetProperty("id").GetInt32(),
            box.ValueKind == JsonValueKind.Array ? new Xfrm(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble()) : null,
            Rgba.FromHex(s.GetProperty("fill").GetString()),
            paras.ValueKind == JsonValueKind.Array
                ? paras.EnumerateArray().Select(p => new ParagraphInfo(p.GetProperty("level").GetInt32(), p.GetProperty("filled").GetBoolean())).ToList()
                : null);
    }

    private static AnimClass ClassFrom(string cls) => cls switch
    {
        "entrance" => AnimClass.Entrance,
        "exit" => AnimClass.Exit,
        "emphasis" => AnimClass.Emphasis,
        "path" => AnimClass.Path,
        "media" => AnimClass.Media,
        _ => throw new ArgumentException(cls),
    };

    private static int? OptInt(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private static IEnumerable<int> Ints(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32());

    private static bool Near(double a, double b) => Math.Abs(a - b) <= Tolerance;

    private static void CompareState(JsonElement want, AnimState got, string where, List<string> errors)
    {
        var mismatches = new List<string>();
        if (want.GetProperty("visible").GetBoolean() != got.Visible)
            mismatches.Add($"visible {got.Visible}");
        void Num(string name, double value)
        {
            var w = want.GetProperty(name).GetDouble();
            if (!Near(w, value))
                mismatches.Add($"{name} {value} != {w}");
        }
        Num("opacity", got.Opacity);
        Num("offsetX", got.OffsetX);
        Num("offsetY", got.OffsetY);
        Num("scaleX", got.ScaleX);
        Num("scaleY", got.ScaleY);
        Num("rotate", got.Rotate);
        var clip = want.GetProperty("clip");
        if (clip.ValueKind == JsonValueKind.Null != (got.Clip is null))
            mismatches.Add($"clip {got.Clip}");
        else if (got.Clip is { } c
            && !(Near(c.L, clip[0].GetDouble()) && Near(c.T, clip[1].GetDouble()) && Near(c.R, clip[2].GetDouble()) && Near(c.B, clip[3].GetDouble())))
            mismatches.Add($"clip {c} != {clip}");
        var tint = Rgba.FromHex(want.GetProperty("tint").GetString());
        if (tint != got.Tint)
            mismatches.Add($"tint {got.Tint?.ToHex()} != {tint?.ToHex()}");
        if (mismatches.Count > 0)
            errors.Add($"{where}: {string.Join(", ", mismatches)}");
    }
}
