using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Animation;
using Uwielbienia.App.Services;
using Uwielbienia.App.Transitions;
using Uwielbienia.Core.Kiosks;
using Uwielbienia.Core.Plans;
using Uwielbienia.Core.Presentation;
using Uwielbienia.Core.Slideshows;
using Uwielbienia.Core.Localization;

namespace Uwielbienia.App.ViewModels;

/// <summary>
/// Kolumna „Pieśń” dla kiosku: nazwa, co ile sekund zmiana i elementy (obraz, prezentacja, napis, obraz z napisem).
/// Każda zmiana zapisuje się od razu w planie, a kiosk na ekranie zmienia się na żywo.
/// </summary>
public sealed partial class KioskViewModel : ObservableObject, IDisposable
{
    private readonly ActivePlan _plan;
    private readonly ISlideshowLibrary _library;
    private readonly bool _loaded;

    public KioskViewModel(KioskPlanItem item, ActivePlan plan, ISlideshowLibrary library)
    {
        Item = item;
        _plan = plan;
        _library = library;
        Title = item.Title;
        Seconds = KioskPlanItemType.Seconds(item);
        SlowZoom = item.SlowZoom;
        foreach (var entry in item.Entries)
            Entries.Add(CreateEntry(entry));
        Transitions.Show(item.ChosenTransitions);
        Transitions.Chosen += (_, _) => Save();
        library.Changed += OnLibraryChanged;
        _loaded = true;
    }

    public KioskPlanItem Item { get; private set; }

    public ObservableCollection<KioskEntryViewModel> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Efekty przejścia do wyboru: żaden — przenikanie, jeden — zawsze ten, kilka — losowo jeden z nich.</summary>
    public TransitionChoiceViewModel Transitions { get; } = new();

    /// <summary>Powolny zoom zdjęć.</summary>
    [ObservableProperty]
    public partial bool SlowZoom { get; set; }

    partial void OnSlowZoomChanged(bool value) => Save();

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial decimal? Seconds { get; set; }

    /// <summary>Nie udało się dodać obrazów albo prezentacji.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    partial void OnTitleChanged(string value) => Save();

    partial void OnSecondsChanged(decimal? value) => Save();

    [RelayCommand]
    private void AddText()
    {
        Entries.Add(CreateEntry(new KioskEntry(Text: "")));
        Save();
    }

    /// <summary>„Obraz”: każdy obraz osobnym elementem (kopie w folderze aplikacji, jak prezentacje).</summary>
    public async Task AddImagesAsync(IReadOnlyList<string> files) =>
        await AddAsync(async () => (await _library.ImportEachAsync(files)).Select(i => i.Folder).ToList());

    /// <summary>„Prezentacja”: jej slajdy (przygotowywane w tle) to kolejne elementy kiosku.</summary>
    public async Task AddPresentationAsync(string file) =>
        await AddAsync(async () => [(await _library.ImportAsync([file])).Folder]);

    private async Task AddAsync(Func<Task<List<string>>> import)
    {
        try
        {
            var folders = await import();
            if (_disposed)
            {
                // Kopiowanie trwało, a operator w tym czasie zaznaczył coś innego — nowe elementy trafiają do aktualnej
                // wersji kiosku w planie (kolumna „Pieśń” wczyta ją od nowa).
                _plan.Update(p => p.Items.FirstOrDefault(i => i.Id == Item.Id) is KioskPlanItem current
                    ? p.Replace(current with { Entries = [.. current.Entries, .. folders.Select(f => new KioskEntry(f))] })
                    : p);
                return;
            }
            foreach (var folder in folders)
                Entries.Add(CreateEntry(new KioskEntry(folder)));
            Error = null;
            Save();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void Remove(KioskEntryViewModel entry)
    {
        if (Entries.Remove(entry))
            Save();
    }

    public void Move(KioskEntryViewModel entry, int newIndex)
    {
        var index = Entries.IndexOf(entry);
        if (index < 0 || newIndex == index)
            return;
        Entries.Move(index, Math.Clamp(newIndex, 0, Entries.Count - 1));
        Save();
    }

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _library.Changed -= OnLibraryChanged;
    }

    private KioskEntryViewModel CreateEntry(KioskEntry entry)
    {
        var vm = new KioskEntryViewModel(entry, _library);
        vm.Edited += (_, _) => Save();
        return vm;
    }

    private void Save()
    {
        OnPropertyChanged(nameof(IsEmpty));
        // pola ustawiane w konstruktorze nie są zmianą; zamknięty model nie nadpisuje planu
        if (!_loaded || _disposed)
            return;
        var title = string.IsNullOrWhiteSpace(Title) ? "Kiosk" : Title.Trim();
        var seconds = Math.Clamp((int)(Seconds ?? KioskPlanItem.DefaultSeconds), KioskPlanItem.MinSeconds, KioskPlanItem.MaxSeconds);
        var transitions = Transitions.Selected;
        var updated = Item with
        {
            Title = title,
            Seconds = seconds,
            Entries = Entries.Select(e => e.Entry).ToList(),
            Transitions = transitions,
            SlowZoom = SlowZoom,
        };
        if (updated.Title == Item.Title && updated.Seconds == Item.Seconds && updated.SlowZoom == Item.SlowZoom
            && updated.Entries.SequenceEqual(Item.Entries) && transitions.SequenceEqual(Item.ChosenTransitions))
            return;
        Item = updated;
        _plan.Update(p => p.Replace(updated));
    }

    private void OnLibraryChanged(object? sender, string folder)
    {
        foreach (var entry in Entries.Where(e => e.Entry.Folder == folder))
            entry.Refresh();
    }
}

/// <summary>Efekt przejścia do zaznaczenia w kiosku.</summary>
public sealed partial class TransitionOptionViewModel(SlideTransition transition, bool selected) : ObservableObject
{
    public SlideTransition Transition { get; } = transition;

    public string Name { get; } = SlideTransitions.Name(transition);

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

/// <summary>
/// Element kiosku w kolumnie „Pieśń”: miniatura obrazu (albo liczba slajdów prezentacji), napisy (jeden albo kilka —
/// zmieniają się same na tle tego samego obrazu) i ich wygląd: położenie, wielkość liter i pokazywanie tekstu.
/// </summary>
public sealed partial class KioskEntryViewModel : ObservableObject, IReorderableItem
{
    private readonly ISlideshowLibrary _library;
    private readonly bool _loaded;

    public KioskEntryViewModel(KioskEntry entry, ISlideshowLibrary library)
    {
        _library = library;
        Entry = entry;
        var style = entry.ChosenStyle;
        IsOver = style.Placement == KioskCaptionPlacement.Over;
        IsTop = entry.Position == KioskCaptionPosition.Top;
        IsMiddle = entry.Position == KioskCaptionPosition.Middle;
        IsBottom = entry.Position == KioskCaptionPosition.Bottom;
        Scale = style.Scale;
        Scrim = style.Scrim;
        IsWord = style.Reveal == KioskTextReveal.Word;
        IsLine = style.Reveal == KioskTextReveal.Line;
        RevealSeconds = (decimal)style.RevealSeconds;
        foreach (var caption in entry.Texts.Count > 0 ? entry.Texts : [new KioskCaption(entry.Title, entry.Text)])
            Captions.Add(Create(caption));
        CaptionsChanged();
        Refresh();
        _loaded = true;
    }

    public KioskEntry Entry { get; private set; }

    /// <summary>Zmieniony napis albo jego wygląd — kiosk zapisuje się w planie.</summary>
    public event EventHandler? Edited;

    public bool HasImage => Entry.Folder is not null;

    public string TextHint => HasImage ? Tr.T("Napis przy obrazie (opcjonalnie)") : Tr.T("Napis");

    /// <summary>Napisy tego elementu w kolejności pokazu.</summary>
    public ObservableCollection<KioskCaptionEditViewModel> Captions { get; } = [];

    /// <summary>Przycisk usuwania napisu pokazuje się dopiero przy kilku napisach.</summary>
    public bool HasManyCaptions => Captions.Count > 1;

    /// <summary>Napis na obrazie (a nie obok niego).</summary>
    [ObservableProperty]
    public partial bool IsOver { get; set; }

    [ObservableProperty]
    public partial bool IsTop { get; set; }

    [ObservableProperty]
    public partial bool IsMiddle { get; set; }

    [ObservableProperty]
    public partial bool IsBottom { get; set; }

    /// <summary>Wielkość liter w procentach.</summary>
    [ObservableProperty]
    public partial decimal? Scale { get; set; }

    /// <summary>Przyciemnienie pod napisem na obrazie.</summary>
    [ObservableProperty]
    public partial bool Scrim { get; set; }

    /// <summary>Tekst pojawia się słowo po słowie.</summary>
    [ObservableProperty]
    public partial bool IsWord { get; set; }

    /// <summary>Tekst pojawia się wiersz po wierszu.</summary>
    [ObservableProperty]
    public partial bool IsLine { get; set; }

    /// <summary>W ile sekund pojawia się cały tekst.</summary>
    [ObservableProperty]
    public partial decimal? RevealSeconds { get; set; }

    /// <summary>Pole „w … s” ma sens tylko przy pokazywaniu po słowie lub wierszu.</summary>
    public bool HasReveal => IsWord || IsLine;

    [RelayCommand]
    private void AddCaption()
    {
        Captions.Add(Create(new KioskCaption()));
        CaptionsChanged();
        Edit();
    }

    [RelayCommand]
    private void RemoveCaption(KioskCaptionEditViewModel caption)
    {
        if (!Captions.Remove(caption))
            return;
        if (Captions.Count == 0)
            Captions.Add(Create(new KioskCaption()));
        CaptionsChanged();
        Edit();
    }

    private KioskCaptionEditViewModel Create(KioskCaption caption)
    {
        var vm = new KioskCaptionEditViewModel(caption);
        vm.Edited += (_, _) => Edit();
        return vm;
    }

    /// <summary>Dodany albo usunięty napis zmienia to, co widać przy każdym z pozostałych.</summary>
    private void CaptionsChanged()
    {
        OnPropertyChanged(nameof(HasManyCaptions));
        foreach (var caption in Captions)
            caption.HasOthers = HasManyCaptions;
    }

    /// <summary>Gdzie stoi napis, wybiera się tylko dla napisu na obrazie i samego napisu (obok obrazu miejsce wynika z proporcji).</summary>
    public bool ShowPosition => IsOver || !HasImage;

    partial void OnIsOverChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPosition));
        Edit();
    }

    partial void OnScaleChanged(decimal? value) => Edit();

    partial void OnScrimChanged(bool value) => Edit();

    partial void OnRevealSecondsChanged(decimal? value) => Edit();

    partial void OnIsTopChanged(bool value) => Pick(value, KioskCaptionPosition.Top);

    partial void OnIsMiddleChanged(bool value) => Pick(value, KioskCaptionPosition.Middle);

    partial void OnIsBottomChanged(bool value) => Pick(value, KioskCaptionPosition.Bottom);

    partial void OnIsWordChanged(bool value) => Reveal(value, KioskTextReveal.Word);

    partial void OnIsLineChanged(bool value) => Reveal(value, KioskTextReveal.Line);

    private bool _picking;

    /// <summary>Trzy przyciski, jeden wybór: zaznaczenie odznacza pozostałe, a odznaczenie ostatniego wraca do dołu.</summary>
    private void Pick(bool value, KioskCaptionPosition position)
    {
        if (_picking)
            return;
        _picking = true;
        if (!value && !IsTop && !IsMiddle && !IsBottom)
            IsBottom = true;
        else if (value)
        {
            IsTop = position == KioskCaptionPosition.Top;
            IsMiddle = position == KioskCaptionPosition.Middle;
            IsBottom = position == KioskCaptionPosition.Bottom;
        }
        _picking = false;
        Edit();
    }

    private void Reveal(bool value, KioskTextReveal reveal)
    {
        if (_picking)
            return;
        _picking = true;
        if (value)
        {
            IsWord = reveal == KioskTextReveal.Word;
            IsLine = reveal == KioskTextReveal.Line;
        }
        _picking = false;
        OnPropertyChanged(nameof(HasReveal));
        Edit();
    }

    [ObservableProperty]
    public partial Task<Bitmap?>? Thumbnail { get; private set; }

    /// <summary>Prezentacja: liczba slajdów; przygotowywanie albo błąd.</summary>
    [ObservableProperty]
    public partial string? Note { get; private set; }

    [ObservableProperty]
    public partial bool IsFailed { get; private set; }

    [ObservableProperty]
    public partial bool IsDragged { get; set; }

    [ObservableProperty]
    public partial bool IsDropBefore { get; set; }

    [ObservableProperty]
    public partial bool IsDropAfter { get; set; }

    private void Edit()
    {
        // pola ustawiane w konstruktorze nie są zmianą
        if (!_loaded)
            return;
        var style = new KioskCaptionStyle(
            IsOver ? KioskCaptionPlacement.Over : KioskCaptionPlacement.Beside,
            IsTop ? KioskCaptionPosition.Top : IsMiddle ? KioskCaptionPosition.Middle : KioskCaptionPosition.Bottom,
            Math.Clamp((int)(Scale ?? 100), KioskCaptionStyle.MinScale, KioskCaptionStyle.MaxScale),
            Scrim,
            IsWord ? KioskTextReveal.Word : IsLine ? KioskTextReveal.Line : KioskTextReveal.Instant,
            (double)Math.Clamp(RevealSeconds ?? 2, 0, 20));
        // nic niewybrane zapisuje się jak dotąd — bez wyglądu (domyślne położenie zależy od tego, czy element ma obraz)
        var untouched = KioskCaptionStyle.Default with { Position = KioskCaptionStyle.PositionOf(null, HasImage) };
        var updated = Entry
            .WithTexts(Captions.Select(c => c.Caption).ToList())
            with
        { Style = style == untouched ? null : style };
        if (updated == Entry)
            return;
        Entry = updated;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh()
    {
        if (Entry.Folder is not { } folder)
            return;
        var state = _library.Peek(folder);
        Thumbnail = state.Slides.Count > 0 ? SlideImages.LoadThumbnailAsync(state.Slides[0]) : null;
        IsFailed = state.IsFailed;
        Note = state.Note ?? (state.Slides.Count > 1 ? Slides(state.Slides.Count) : null);
    }

    private static string Slides(int n) =>
        n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? Tr.F("{0} slajdy — każdy osobno", n) : Tr.F("{0} slajdów — każdy osobno", n);
}

/// <summary>Jeden napis elementu kiosku w edytorze: tytuł, treść i własny czas (puste = czas kiosku).</summary>
public sealed partial class KioskCaptionEditViewModel : ObservableObject
{
    private readonly bool _loaded;

    public KioskCaptionEditViewModel(KioskCaption caption)
    {
        Caption = caption;
        Title = caption.Title ?? "";
        Text = caption.Text ?? "";
        Seconds = caption.Seconds;
        _loaded = true;
    }

    public KioskCaption Caption { get; private set; }

    public event EventHandler? Edited;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>Czas tego napisu; puste = czas kiosku.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSeconds))]
    public partial int? Seconds { get; set; }

    /// <summary>Element ma więcej napisów niż ten jeden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSeconds))]
    public partial bool HasOthers { get; set; }

    /// <summary>
    /// Czas napisu ustawia się przy kilku napisach, a także gdy ten jeden ma własny czas — inaczej czas zostałby
    /// ukryty po usunięciu pozostałych napisów i nie dałoby się go wyczyścić.
    /// </summary>
    public bool ShowSeconds => HasOthers || Seconds is not null;

    partial void OnTitleChanged(string value) => Edit();

    partial void OnTextChanged(string value) => Edit();

    partial void OnSecondsChanged(int? value) => Edit();

    private void Edit()
    {
        if (!_loaded)
            return;
        var seconds = Seconds is { } s ? Math.Clamp(s, KioskPlanItem.MinSeconds, KioskPlanItem.MaxSeconds) : (int?)null;
        var updated = new KioskCaption(Title.Length == 0 ? null : Title, Text.Length == 0 ? null : Text, seconds);
        if (updated == Caption)
            return;
        Caption = updated;
        Edited?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Kiosk na rzutniku i w kolumnie „Na ekranie”. Strona równa poprzedniej, dopóki na ekranie jest ten sam kiosk — elementy
/// przenikają się w niej same (<see cref="KioskPlayback.Frame"/>), bez ponownego przejścia całej strony.
/// </summary>
public sealed record ProjectedKiosk(KioskSlide Kiosk, KioskPlayback Playback) : ProjectedPage(Kiosk, null), IWaitablePage
{
    /// <summary>Przejście na kiosk czeka na jego pierwszy element — inaczej efekt prowadziłby do pustego tła.</summary>
    public Task Ready => Playback.Ready;

    // Równa każdej innej stronie tego kiosku — także po edycji treści na żywo (inny KioskSlide): strona nie przenika się
    // i nie buduje od nowa (zoom zdjęcia nie skacze); zmiany elementów przychodzą przez Playback.
    public bool Equals(ProjectedKiosk? other) => other is not null && ReferenceEquals(Playback, other.Playback);

    public override int GetHashCode() => Playback.GetHashCode();
}

/// <summary>
/// Bieżący element kiosku dla widoków — z <see cref="KioskRotation"/> (jednej dla wszystkich ekranów) — i przejście między
/// elementami (<see cref="ShaderPageTransition"/> z efektów wybranych w kiosku).
/// </summary>
public sealed partial class KioskPlayback : ObservableObject
{
    private readonly KioskRotation _rotation;
    private int _version;

    /// <summary>Numer klatki pokazanej w bieżącej stronie; <c>-1</c> = strony jeszcze nie ma.</summary>
    private int _shownIndex = -1;

    public KioskPlayback(KioskRotation rotation)
    {
        _rotation = rotation;
        rotation.Changed += (_, _) => _ = ApplyAsync();
        _ = ApplyAsync();
    }

    [ObservableProperty]
    public partial KioskFramePage? Frame { get; private set; }

    /// <summary>Wczytanie elementu, który zaraz będzie <see cref="Frame"/> (obraz i układ).</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial IPageTransition Transition { get; private set; } = ShaderPageTransition.For([]);

    /// <summary>Dla operatora, np. „2 z 5 · co 8 s” — czas bieżącego elementu, bo napis może mieć własny.</summary>
    public string? PositionText => _rotation.Slide is { } slide
        ? Tr.F("{0} z {1} · co {2:0} s", _rotation.Index + 1, _rotation.Count, slide.Duration(_rotation.Index).TotalSeconds) + (_rotation.IsRunning || _rotation.Count < 2 ? "" : Tr.T(" · zatrzymany"))
        : null;

    /// <summary>Nowy element pokazuje się dopiero z wczytanym obrazem — przejście ma co sfotografować.</summary>
    private async Task ApplyAsync()
    {
        var version = ++_version;
        OnPropertyChanged(nameof(PositionText));
        if (_rotation.Slide is { } slide && (Transition is not ShaderPageTransition current
                ? slide.ChosenTransitions.Count > 0
                : !current.Effects.SequenceEqual(slide.ChosenTransitions)))
            Transition = ShaderPageTransition.For(slide.ChosenTransitions);
        if (_rotation.Frame is not { } frame)
        {
            Frame = null;
            return;
        }
        var index = _rotation.Index;
        // zoom trwa przez wszystkie napisy tego samego zdjęcia
        var zoom = _rotation.Slide is { SlowZoom: true } shown ? shown.RunDuration(index) + ZoomOverlap : TimeSpan.Zero;
        // Kolejny napis tego samego obrazu zmienia się w tej samej stronie: zdjęcie zostaje, zoom leci dalej, przenika sam napis.
        if (Frame is { } showing && _rotation.Slide is { } shownSlide && shownSlide.ContinuesRun(_shownIndex, index) && showing.CanShow(frame))
        {
            showing.Show(frame);
            _shownIndex = index;
        }
        // Nowa strona tylko dla innego elementu — zmiana czasu albo zoomu działa od następnego (bez przejścia do tego samego).
        else if (Frame?.Frame != frame)
        {
            var page = new KioskFramePage(frame, zoom);
            Ready = page.Ready;
            await page.Ready;
            if (version == _version)
            {
                Frame = page;
                _shownIndex = index;
            }
        }
        // następny obraz wczytany zawczasu — zmiana bez czekania
        if (_rotation is { Slide: { } next, Count: > 1 } && next.Frames[(_rotation.Index + 1) % _rotation.Count].Image is { } file)
            SlideImages.Preload(file);
    }

    /// <summary>Zoom trwa dłużej niż element — nie staje przed przejściem do następnego.</summary>
    private static readonly TimeSpan ZoomOverlap = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Element kiosku na płótnie 1920×1080: obraz na cały ekran, sam napis, albo obraz z napisem z prawej lub pod spodem —
/// wg proporcji obrazu (<see cref="KioskFrame.LayoutFor"/>), więc układ jest znany dopiero po wczytaniu obrazu (<see cref="Ready"/>).
/// </summary>
public sealed partial class KioskFramePage : ObservableObject
{
    /// <summary>Wielkość liter napisu na płótnie 1920×1080 — sam napis, obok obrazu, na obrazie.</summary>
    private const double FullSize = 86, SideSize = 60, OverSize = 76;

    /// <param name="zoomDuration">Powolny zoom zdjęcia (<see cref="Views.SlowZoom"/>); zero = bez.</param>
    public KioskFramePage(KioskFrame frame, TimeSpan zoomDuration = default)
    {
        Frame = frame;
        ZoomDuration = zoomDuration;
        if (frame.Image is { } image)
            Ready = LoadAsync(image);
        else
        {
            Layout = KioskLayout.Text;
            Caption = CaptionFor(frame);
            Ready = Task.CompletedTask;
        }
    }

    [ObservableProperty]
    public partial KioskFrame Frame { get; private set; }

    public TimeSpan ZoomDuration { get; }

    /// <summary>Obraz wczytany i układ znany.</summary>
    public Task Ready { get; }

    /// <summary>Napis na stronie; zmienia się przy kolejnym napisie tego samego obrazu (przenika w miejscu).</summary>
    [ObservableProperty]
    public partial KioskCaptionView? Caption { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageOnly), nameof(IsTextOnly), nameof(IsTextRight), nameof(IsTextBelow), nameof(IsTextOver), nameof(Position), nameof(Alignment), nameof(HasScrim), nameof(HasEdgeScrim), nameof(HasMiddleScrim))]
    public partial KioskLayout? Layout { get; private set; }

    [ObservableProperty]
    public partial Bitmap? Picture { get; private set; }

    public bool IsImageOnly => Layout == KioskLayout.Image;

    public bool IsTextOnly => Layout == KioskLayout.Text;

    public bool IsTextRight => Layout == KioskLayout.TextRight;

    public bool IsTextBelow => Layout == KioskLayout.TextBelow;

    /// <summary>Napis na obrazie (obraz na cały ekran).</summary>
    public bool IsTextOver => Layout == KioskLayout.TextOver;

    /// <summary>
    /// Gdzie stoi napis: wybór operatora, a bez niego środek dla samego napisu (także gdy zdjęcia nie dało się wczytać)
    /// i dół na zdjęciu.
    /// </summary>
    public KioskCaptionPosition Position => KioskCaptionStyle.PositionOf(Frame.Style, Layout != KioskLayout.Text);

    /// <summary>Napis u góry, na środku albo u dołu ekranu — dla napisu na obrazie i samego napisu.</summary>
    public VerticalAlignment Alignment => Position switch
    {
        KioskCaptionPosition.Top => VerticalAlignment.Top,
        KioskCaptionPosition.Bottom => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center,
    };

    /// <summary>Przyciemnienie pod napisem na obrazie — od strony, przy której stoi napis.</summary>
    public bool HasScrim => IsTextOver && Frame.ChosenStyle.Scrim;

    /// <summary>Przyciemnienie od góry albo od dołu (napis przy krawędzi).</summary>
    public bool HasEdgeScrim => HasScrim && Position != KioskCaptionPosition.Middle;

    /// <summary>Przyciemnienie na całym obrazie (napis na środku).</summary>
    public bool HasMiddleScrim => HasScrim && Position == KioskCaptionPosition.Middle;

    public IBrush Scrim => Position switch
    {
        KioskCaptionPosition.Top => Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)),
        KioskCaptionPosition.Bottom => Gradient(new RelativePoint(0, 1, RelativeUnit.Relative), new RelativePoint(0, 0, RelativeUnit.Relative)),
        _ => new RadialGradientBrush
        {
            GradientStops = [new GradientStop(Color.FromArgb(0xB8, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x8C, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0x2E, 0, 0, 0), 1)],
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
        },
    };

    private static LinearGradientBrush Gradient(RelativePoint from, RelativePoint to) => new()
    {
        StartPoint = from,
        EndPoint = to,
        GradientStops =
        [
            new GradientStop(Color.FromArgb(0xD1, 0, 0, 0), 0),
            new GradientStop(Color.FromArgb(0x9E, 0, 0, 0), 0.38),
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
        ],
    };

    /// <summary>Kolejny napis tego samego obrazu (ten sam wygląd) — zmienia się w tej samej stronie.</summary>
    public bool CanShow(KioskFrame next) =>
        Layout is not null && next.Image is not null && next.Image == Frame.Image
        && next.ChosenStyle == Frame.ChosenStyle && next.HasText && Frame.HasText;

    public void Show(KioskFrame next)
    {
        if (next == Frame)
            return;
        Frame = next;
        Caption = CaptionFor(next);
    }

    private KioskCaptionView CaptionFor(KioskFrame frame)
    {
        var size = Layout switch
        {
            KioskLayout.TextRight or KioskLayout.TextBelow => SideSize,
            KioskLayout.TextOver => OverSize,
            _ => FullSize,
        };
        return new KioskCaptionView(frame, size * frame.ChosenStyle.Zoom);
    }

    private async Task LoadAsync(string image)
    {
        var picture = await SlideImages.LoadAsync(image);
        Picture = picture;
        // brak obrazu (uszkodzony plik) — sam napis, a bez napisu czarny ekran
        Layout = picture is null
            ? Frame.HasText ? KioskLayout.Text : null
            : Frame.LayoutFor(picture.Size.Width / picture.Size.Height);
        Caption = Layout is null ? null : CaptionFor(Frame);
    }
}

/// <summary>
/// Napis elementu kiosku: tytuł nad tekstem, wielkość liter wg układu i ustawienia elementu, a tekst pojawia się od razu
/// albo słowo po słowie (<see cref="Views.RevealText"/>). Osobny obiekt, bo przy kilku napisach jednego zdjęcia przenika
/// się tylko on — zdjęcie zostaje na ekranie.
/// </summary>
public sealed class KioskCaptionView
{
    public KioskCaptionView(KioskFrame frame, double fontSize)
    {
        Title = frame.Title;
        Text = frame.Text;
        FontSize = fontSize;
        Reveal = frame.ChosenStyle.Reveal;
        var all = Words(Title) + Words(Text);
        var reveal = frame.ChosenStyle.Reveals;
        // słowa tytułu i tekstu dzielą czas po równo — napis składa się jako całość
        TitleDuration = all == 0 ? TimeSpan.Zero : reveal * (Words(Title) / (double)all);
        TextDuration = reveal - TitleDuration;
    }

    public string? Title { get; }

    public string? Text { get; }

    public bool HasTitle => Title is not null;

    public bool HasBody => Text is not null;

    public double FontSize { get; }

    public KioskTextReveal Reveal { get; }

    public TimeSpan TitleDuration { get; }

    public TimeSpan TextDuration { get; }

    /// <summary>Tekst zaczyna się po tytule.</summary>
    public TimeSpan TextDelay => TitleDuration;

    private static int Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}
