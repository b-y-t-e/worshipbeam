using System.Text.Json.Serialization;
using Uwielbienia.Core.AppWindows;
using Uwielbienia.Core.Presentation;
using Uwielbienia.Core.Songs;

namespace Uwielbienia.Core.Plans;

public enum PlanKind
{
    /// <summary>Konkretne wydarzenie, np. „Uwielbienie 25 września 2026”.</summary>
    Event,
    /// <summary>Szablon do wielokrotnego użycia, np. „Próba z dziećmi”.</summary>
    Template,
}

/// <summary>Plan (lista pozycji) na wydarzenie albo szablon.</summary>
/// <param name="Transitions">
/// Efekty przejścia z jednej pozycji planu na inną (rzutnik; kiosk ma własne — <see cref="KioskPlanItem.Transitions"/>) —
/// przy kilku losowany jest jeden; pusta lista = zwykłe przenikanie; <c>null</c> = domyślne (<see cref="DefaultTransitions"/>).
/// </param>
/// <param name="InnerTransitions">
/// Efekty przejścia między slajdami jednej pozycji (zwrotki pieśni, slajdy prezentacji); <c>null</c> = jak między pozycjami.
/// </param>
public sealed record Plan(
    Guid Id,
    string Name,
    DateOnly? Date,
    PlanKind Kind,
    IReadOnlyList<PlanItem> Items,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SlideTransition>? Transitions = null,
    IReadOnlyList<SlideTransition>? InnerTransitions = null)
{
    public static IReadOnlyList<SlideTransition> DefaultTransitions => SlideTransitions.Default;

    public IReadOnlyList<SlideTransition> ChosenTransitions => Transitions ?? DefaultTransitions;

    public IReadOnlyList<SlideTransition> ChosenInnerTransitions => InnerTransitions ?? ChosenTransitions;

    /// <summary>Przejście między slajdami pozycji: własne efekty albo <c>null</c> = jak między pozycjami.</summary>
    public Plan WithInnerTransitions(IReadOnlyList<SlideTransition>? chosen) =>
        this with { InnerTransitions = chosen, UpdatedAt = DateTimeOffset.Now };

    /// <summary>Nowy wybór efektów przejścia (równy domyślnemu — plan podąża za domyślnymi).</summary>
    public Plan WithTransitions(IReadOnlyList<SlideTransition> chosen) =>
        this with { Transitions = SlideTransitions.ToStored(chosen), UpdatedAt = DateTimeOffset.Now };

    public static Plan Create(string name, DateOnly? date, PlanKind kind = PlanKind.Event) =>
        new(Guid.NewGuid(), name, date, kind, [], DateTimeOffset.Now);

    /// <summary>Kopia z nowymi identyfikatorami — „Użyj ponownie” albo „Zapisz jako szablon”.</summary>
    public Plan CloneAs(string name, DateOnly? date, PlanKind kind) =>
        new(Guid.NewGuid(), name, date, kind, Items.Select(i => i.WithNewId()).ToList(), DateTimeOffset.Now, Transitions, InnerTransitions);

    public Plan WithItems(IReadOnlyList<PlanItem> items) => this with { Items = items, UpdatedAt = DateTimeOffset.Now };

    public Plan Insert(int index, PlanItem item)
    {
        var items = Items.ToList();
        items.Insert(Math.Clamp(index, 0, items.Count), item);
        return WithItems(items);
    }

    /// <summary>Wstawia zaraz za pozycją <paramref name="itemId"/> (np. tą na ekranie), a gdy jej nie ma — na koniec.</summary>
    public Plan InsertAfter(Guid? itemId, PlanItem item) =>
        Insert(itemId is { } id && IndexOf(id) is >= 0 and var index ? index + 1 : Items.Count, item);

    public Plan Remove(Guid itemId) => WithItems(Items.Where(i => i.Id != itemId).ToList());

    public Plan Replace(PlanItem item) => WithItems(Items.Select(i => i.Id == item.Id ? item : i).ToList());

    public Plan Move(Guid itemId, int newIndex)
    {
        var items = Items.ToList();
        var item = items.Single(i => i.Id == itemId);
        items.Remove(item);
        items.Insert(Math.Clamp(newIndex, 0, items.Count), item);
        return WithItems(items);
    }

    public int IndexOf(Guid itemId) => Items.ToList().FindIndex(i => i.Id == itemId);
}

/// <summary>
/// Pozycja planu: pieśń lub tekst (<see cref="SongPlanItem"/>), prezentacja (<see cref="PresentationPlanItem"/>),
/// film (<see cref="VideoPlanItem"/>), kiosk (<see cref="KioskPlanItem"/>), aplikacja (<see cref="AppWindowPlanItem"/>).
/// Nowy rodzaj = nowy rekord z <see cref="JsonDerivedTypeAttribute"/> (CLAUDE.md: „Nowy rodzaj elementu”).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SongPlanItem), "song")]
[JsonDerivedType(typeof(PresentationPlanItem), "presentation")]
[JsonDerivedType(typeof(VideoPlanItem), "video")]
[JsonDerivedType(typeof(KioskPlanItem), "kiosk")]
[JsonDerivedType(typeof(AppWindowPlanItem), "app")]
public abstract record PlanItem(Guid Id)
{
    public abstract PlanItem WithNewId();

    /// <summary>Własne efekty przejścia między slajdami tej pozycji; <c>null</c> = przejście slajdów planu.</summary>
    [JsonIgnore]
    public virtual IReadOnlyList<SlideTransition>? InnerTransitions => null;
}

/// <summary>Część w układzie pieśni: kod sekcji i czy jest śpiewana (odznaczona = pominięta).</summary>
public sealed record LayoutEntry(string Code, bool Sung = true);

/// <summary>Pieśń w planie z wybranymi częściami w wybranej kolejności.</summary>
/// <param name="Arrangement">Kody sekcji do zaśpiewania; <c>null</c> = kolejność z pliku pieśni.</param>
/// <param name="Layout">
/// Pełny układ pieśni w tym planie (kolejność, powtórzenia, części pominięte); <c>null</c> = wynika z
/// <paramref name="Arrangement"/> i kolejności z pliku. <paramref name="Arrangement"/> to zawsze jego śpiewana część.
/// </param>
/// <param name="Removed">
/// Części usunięte z układu w tym planie („Usuń”). Odróżnia je od części dopisanych później w edytorze
/// (albo w nowej wersji śpiewnika), które pojawiają się w układzie same. <c>null</c> = plan sprzed tego pola.
/// </param>
/// <param name="SlideEffects">
/// Własne efekty przejścia między slajdami tej pieśni (jak <see cref="Plan.InnerTransitions"/>); <c>null</c> = przejście
/// slajdów planu.
/// </param>
public sealed record SongPlanItem(
    Guid Id,
    string SongId,
    IReadOnlyList<string>? Arrangement,
    IReadOnlyList<LayoutEntry>? Layout = null,
    IReadOnlyList<string>? Removed = null,
    IReadOnlyList<SlideTransition>? SlideEffects = null)
    : PlanItem(Id)
{
    [JsonIgnore]
    public override IReadOnlyList<SlideTransition>? InnerTransitions => SlideEffects;

    /// <summary>
    /// Układ do wyświetlenia w kolumnie „Pieśń”, uzgodniony z aktualną treścią pieśni: bez części, których już nie
    /// ma, z nowymi częściami (śpiewanymi, na końcu), które nie zostały usunięte w planie.
    /// Tekst ma zawsze kolejność z edytora — plan pamięta tylko pominięte i usunięte części.
    /// </summary>
    public IReadOnlyList<LayoutEntry> LayoutFor(Song song)
    {
        if (Layout is null)
        {
            if (Arrangement is null)
                return song.Arrangement.Select(code => new LayoutEntry(code)).ToList();
            // Dawniej zapisywano tylko śpiewane części w kolejności pliku — pozostałe są pominięte.
            var remaining = Arrangement.ToList();
            return song.Arrangement.Select(code => new LayoutEntry(code, remaining.Remove(code))).ToList();
        }

        var existing = song.Sections.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        var removed = (Removed ?? []).ToHashSet(StringComparer.Ordinal);
        var layout = Layout.Where(e => existing.Contains(e.Code)).ToList();
        if (song.IsText)
        {
            var sung = layout.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First().Sung, StringComparer.Ordinal);
            return song.Arrangement.Distinct().Where(code => !removed.Contains(code))
                .Select(code => new LayoutEntry(code, sung.GetValueOrDefault(code, true))).ToList();
        }

        // nowe części na końcu układu (jak „Przywróć usuniętą część”), w kolejności z pliku
        var known = layout.Select(e => e.Code).Concat(removed).ToHashSet(StringComparer.Ordinal);
        layout.AddRange(song.Arrangement.Where(known.Add).Select(code => new LayoutEntry(code)));
        return layout;
    }

    /// <summary>Kody do zaśpiewania (ekran): śpiewana część uzgodnionego układu.</summary>
    public IReadOnlyList<string> ArrangementFor(Song song) =>
        Layout is null && Arrangement is null
            ? song.Arrangement
            : LayoutFor(song).Where(e => e.Sung).Select(e => e.Code).ToList();

    /// <summary>
    /// Nowy układ; części pieśni, których w nim nie ma, są usunięte w tym planie. Gdy układ jest taki jak w pliku
    /// pieśni, pozycja znowu podąża za plikiem.
    /// </summary>
    public SongPlanItem WithLayout(Song song, IReadOnlyList<LayoutEntry> layout)
    {
        var removed = song.Arrangement.Distinct().Where(code => layout.All(e => e.Code != code)).ToList();
        var isDefault = removed.Count == 0 && layout.All(e => e.Sung)
                        && (song.IsText || layout.Select(e => e.Code).SequenceEqual(song.Arrangement));
        return isDefault
            ? this with { Arrangement = null, Layout = null, Removed = null }
            : this with { Arrangement = layout.Where(e => e.Sung).Select(e => e.Code).ToList(), Layout = layout, Removed = removed };
    }

    public static SongPlanItem For(string songId) => new(Guid.NewGuid(), songId, null);

    public override PlanItem WithNewId() => this with { Id = Guid.NewGuid() };
}

/// <summary>Prezentacja (PowerPoint albo obrazy) skopiowana do folderu aplikacji.</summary>
/// <param name="Folder">Folder prezentacji w katalogu aplikacji (<see cref="Slideshows.ISlideshowLibrary"/>).</param>
/// <param name="SlideEffects">
/// Własne efekty przejścia między slajdami prezentacji (jak <see cref="Plan.InnerTransitions"/>); <c>null</c> = przejście
/// slajdów planu.
/// </param>
public sealed record PresentationPlanItem(
    Guid Id,
    string Folder,
    string Title,
    IReadOnlyList<SlideTransition>? SlideEffects = null) : PlanItem(Id)
{
    /// <summary>Nowo dodana prezentacja: slajdy bez efektu przejścia (jak w PowerPoincie — kliknięcie = następny slajd od razu).</summary>
    public static PresentationPlanItem Added(string folder, string title) =>
        new(Guid.NewGuid(), folder, title, SlideTransitions.Cut);

    public override PlanItem WithNewId() => this with { Id = Guid.NewGuid() };

    [JsonIgnore]
    public override IReadOnlyList<SlideTransition>? InnerTransitions => SlideEffects;
}

/// <summary>Film wskazany na dysku (nie jest kopiowany — pliki filmów bywają bardzo duże).</summary>
/// <param name="File">Pełna ścieżka pliku filmu.</param>
public sealed record VideoPlanItem(Guid Id, string File, string Title) : PlanItem(Id)
{
    public static VideoPlanItem For(string file) => new(Guid.NewGuid(), file, Path.GetFileNameWithoutExtension(file));

    public override PlanItem WithNewId() => this with { Id = Guid.NewGuid() };
}

/// <summary>Aplikacja: obraz okna innego programu (np. „Ekran bezprzewodowy”), dopóki pozycja jest na ekranie.</summary>
/// <param name="Process">Nazwa procesu programu (bez <c>.exe</c>).</param>
/// <param name="WindowTitle">Tytuł okna — tylko dla aplikacji ze Sklepu (<see cref="AppWindowTarget"/>); <c>null</c> = dowolne okno.</param>
public sealed record AppWindowPlanItem(Guid Id, string Title, string Process, string? WindowTitle) : PlanItem(Id)
{
    public static AppWindowPlanItem For(AppWindowInfo window)
    {
        var target = AppWindowTarget.For(window);
        return new(Guid.NewGuid(), window.Title.Length > 0 ? window.Title : window.Process, target.Process, target.Title);
    }

    public override PlanItem WithNewId() => this with { Id = Guid.NewGuid() };
}

/// <summary>
/// Kiosk: obrazy i napisy, które zmieniają się same co <see cref="Seconds"/> sekund (w kółko). Dla planu i pilota to jeden
/// slajd — „Dalej” przechodzi do następnej pozycji planu.
/// </summary>
/// <param name="Seconds">Co ile sekund następny element.</param>
/// <param name="Transitions">
/// Efekty przejścia między elementami — przy kilku losowany jest jeden; pusta lista = zwykłe przenikanie;
/// <c>null</c> = domyślne (<see cref="DefaultTransitions"/>). Niezależne od przejść planu.
/// </param>
/// <param name="SlowZoom">Zdjęcia powoli przybliżają się i przesuwają, póki są na ekranie.</param>
public sealed record KioskPlanItem(
    Guid Id,
    string Title,
    IReadOnlyList<KioskEntry> Entries,
    int Seconds = KioskPlanItem.DefaultSeconds,
    IReadOnlyList<SlideTransition>? Transitions = null,
    bool SlowZoom = true)
    : PlanItem(Id)
{
    public const int DefaultSeconds = 8;
    public const int MinSeconds = 2;
    public const int MaxSeconds = 600;

    public static IReadOnlyList<SlideTransition> DefaultTransitions => SlideTransitions.Default;

    public IReadOnlyList<SlideTransition> ChosenTransitions => Transitions ?? DefaultTransitions;

    public static KioskPlanItem Create(string title = "Kiosk") => new(Guid.NewGuid(), title, []);

    // Foldery obrazów się nie zmieniają (edycja kiosku tylko dodaje nowe) — kopia może je współdzielić.
    public override PlanItem WithNewId() => this with { Id = Guid.NewGuid() };
}

/// <summary>Element kiosku: obraz (albo wszystkie slajdy prezentacji), napis, albo obraz z jednym lub kilkoma napisami.</summary>
/// <param name="Folder">Folder obrazu lub prezentacji w katalogu aplikacji (<see cref="Slideshows.ISlideshowLibrary"/>); <c>null</c> = sam napis.</param>
/// <param name="Title">Tytuł pierwszego napisu (opcjonalny); kilka napisów zapisuje <paramref name="Captions"/>.</param>
/// <param name="Text">Treść pierwszego napisu; wiersze rozdziela znak nowej linii.</param>
/// <param name="Captions">
/// Napisy tego elementu, gdy jest ich kilka — zmieniają się same na tle tego samego obrazu, a po ostatnim kiosk
/// przechodzi do następnego elementu. <c>null</c> (zwykły przypadek, też wszystkie starsze plany) = jeden napis
/// z <paramref name="Title"/> i <paramref name="Text"/>.
/// </param>
/// <param name="Style">Wygląd napisu (położenie, wielkość, pokazywanie tekstu); <c>null</c> = domyślny.</param>
public sealed record KioskEntry(
    string? Folder = null,
    string? Title = null,
    string? Text = null,
    IReadOnlyList<KioskCaption>? Captions = null,
    KioskCaptionStyle? Style = null)
{
    public bool HasText => Texts.Count > 0;

    /// <summary>Napisy w kolejności pokazu — z <see cref="Captions"/> albo jeden z <see cref="Title"/> i <see cref="Text"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<KioskCaption> Texts => Captions is { Count: > 0 } captions
        ? captions.Where(c => c.HasText).ToList()
        : new KioskCaption(Title, Text) is { HasText: true } single ? [single] : [];

    /// <summary>Wygląd napisu z rozstrzygniętym położeniem — takim, jakie element naprawdę ma na ekranie.</summary>
    [JsonIgnore]
    public KioskCaptionStyle ChosenStyle => (Style ?? KioskCaptionStyle.Default) with { Position = Position };

    /// <summary>Gdzie stoi napis tego elementu (<see cref="KioskCaptionStyle.PositionOf"/>).</summary>
    [JsonIgnore]
    public KioskCaptionPosition Position => KioskCaptionStyle.PositionOf(Style, Folder is not null);

    /// <summary>Napisy w jednym polu: jeden zapisuje się jak dawniej (<see cref="Title"/>, <see cref="Text"/>).</summary>
    public KioskEntry WithTexts(IReadOnlyList<KioskCaption> texts)
    {
        var kept = texts.Where(c => c.HasText).ToList();
        return kept switch
        {
            [] => this with { Title = null, Text = null, Captions = null },
            [{ Seconds: null } one] => this with { Title = one.Title, Text = one.Text, Captions = null },
            _ => this with { Title = null, Text = null, Captions = kept },
        };
    }

    // Porównanie treścią napisów i wyglądem (rekord porównałby odwołanie do listy) — inaczej każdy zapis kiosku
    // wyglądałby na zmianę. Hasz liczy się z tego samego, co porównanie.
    public bool Equals(KioskEntry? other) =>
        other is not null && Folder == other.Folder && ChosenStyle == other.ChosenStyle && Texts.SequenceEqual(other.Texts);

    public override int GetHashCode() => HashCode.Combine(Folder, ChosenStyle, Texts.Count);
}

/// <summary>Jeden napis elementu kiosku.</summary>
/// <param name="Title">Tytuł (opcjonalny).</param>
/// <param name="Text">Treść; wiersze rozdziela znak nowej linii.</param>
/// <param name="Seconds">Jak długo ten napis jest widoczny; <c>null</c> = czas kiosku (<see cref="KioskPlanItem.Seconds"/>).</param>
public sealed record KioskCaption(string? Title = null, string? Text = null, int? Seconds = null)
{
    [JsonIgnore]
    public bool HasText => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Gdzie i jak pokazuje się napis elementu kiosku.</summary>
/// <param name="Placement">Napis obok obrazu albo na nim.</param>
/// <param name="Position">Napis u góry, na środku albo u dołu (przy napisie na obrazie i samym napisie).</param>
/// <param name="Scale">Wielkość liter w procentach zwykłej (50–300).</param>
/// <param name="Scrim">Przyciemnienie pod napisem na obrazie — bez niego napis ginie na jasnym zdjęciu.</param>
/// <param name="Reveal">Cały napis od razu, słowo po słowie albo wiersz po wierszu.</param>
/// <param name="RevealSeconds">W ile sekund pojawiają się wszystkie słowa (nie wydłuża napisu).</param>
public sealed record KioskCaptionStyle(
    KioskCaptionPlacement Placement = KioskCaptionPlacement.Beside,
    KioskCaptionPosition Position = KioskCaptionPosition.Bottom,
    int Scale = 100,
    bool Scrim = true,
    KioskTextReveal Reveal = KioskTextReveal.Instant,
    double RevealSeconds = 2)
{
    public const int MinScale = 50, MaxScale = 300;

    public static KioskCaptionStyle Default { get; } = new();

    /// <summary>
    /// Gdzie stoi napis: wybór operatora, a bez niego (element bez własnego wyglądu, też z planu sprzed tego ustawienia)
    /// środek ekranu dla samego napisu — jak dotąd — i dół przy zdjęciu.
    /// </summary>
    public static KioskCaptionPosition PositionOf(KioskCaptionStyle? style, bool withImage) =>
        style?.Position ?? (withImage ? Default.Position : KioskCaptionPosition.Middle);

    /// <summary>Mnożnik wielkości liter (1 = zwykła).</summary>
    [JsonIgnore]
    public double Zoom => Math.Clamp(Scale, MinScale, MaxScale) / 100.0;

    /// <summary>Czas pojawiania się słów; zero = cały napis od razu.</summary>
    [JsonIgnore]
    public TimeSpan Reveals => Reveal == KioskTextReveal.Instant
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Math.Clamp(RevealSeconds, 0, 20));
}

/// <summary>Napis obok obrazu (jak dotąd) albo na nim.</summary>
public enum KioskCaptionPlacement
{
    /// <summary>Obraz w ramce, napis z prawej albo pod spodem — wg proporcji obrazu.</summary>
    Beside,

    /// <summary>Obraz na cały ekran (z powolnym zoomem), napis na nim.</summary>
    Over,
}

/// <summary>Gdzie na ekranie stoi napis.</summary>
public enum KioskCaptionPosition
{
    Top,
    Middle,
    Bottom,
}

/// <summary>Jak pojawia się tekst napisu.</summary>
public enum KioskTextReveal
{
    /// <summary>Cały napis od razu.</summary>
    Instant,

    /// <summary>Słowo po słowie, wszystkie w zadanym czasie.</summary>
    Word,

    /// <summary>Wiersz po wierszu.</summary>
    Line,
}
