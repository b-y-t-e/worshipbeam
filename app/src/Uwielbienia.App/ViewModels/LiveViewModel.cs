using System.Collections.ObjectModel;
using Avalonia.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Uwielbienia.App.Services;
using Uwielbienia.App.Transitions;
using Uwielbienia.Core.Kiosks;
using Uwielbienia.Core.Presentation;
using Uwielbienia.Core.Slideshows;
using Uwielbienia.Core.Localization;

namespace Uwielbienia.App.ViewModels;

/// <summary>Panel „Na ekranie”: dokładnie to, co widzi sala, plus akordy i skróty do slajdów bieżącej pieśni.</summary>
public sealed partial class LiveViewModel : ObservableObject
{
    private readonly ILiveControl _control;

    public LiveViewModel(
        ILiveStateSource source, ILiveControl control, ProjectionAppearance appearance, VideoControlsViewModel video, KioskPlayback kiosk,
        AppWindowLiveViewModel appWindow)
    {
        _control = control;
        Appearance = appearance;
        Video = video;
        Kiosk = kiosk;
        AppWindow = appWindow;
        kiosk.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CurrentLabel));
        source.StateChanged += (_, state) => Apply(state);
        Apply(source.State);
    }

    public ProjectionAppearance Appearance { get; }

    /// <summary>Film na ekranie: podgląd i sterowanie odtwarzaniem.</summary>
    public VideoControlsViewModel Video { get; }

    /// <summary>Kiosk na ekranie: bieżący element (zmienia się sam).</summary>
    public KioskPlayback Kiosk { get; }

    /// <summary>Aplikacja na ekranie: obraz okna programu.</summary>
    public AppWindowLiveViewModel AppWindow { get; }

    [ObservableProperty]
    public partial LiveState State { get; private set; } = LiveState.Empty;

    public Slide? VisibleSlide => State.VisibleSlide;

    /// <summary>Strona rzutnika: slajd i — na pierwszym slajdzie pieśni — jej tytuł; slajd prezentacji — obraz; film.</summary>
    public ProjectedPage? Page => State.VisibleSlide switch
    {
        ImageSlide image => new ProjectedImage(image, _motion),
        VideoSlide video => new ProjectedVideo(video, Video.Frames),
        AppWindowSlide app => new ProjectedAppWindow(app, AppWindow.Frames),
        KioskSlide kiosk => new ProjectedKiosk(kiosk, Kiosk),
        { } slide when State.Item is { } item => new ProjectedPage(slide, State.SlideIndex == 0 && item.ShowTitle ? item.Title : null),
        _ => null,
    };

    /// <summary>Ten sam slajd, czarny ekran, pieśń spoza planu i wybrane samo przenikanie: krótkie przenikanie.</summary>
    public static readonly IPageTransition SlideChange = new DirectCrossFade(TimeSpan.FromSeconds(0.15));

    /// <summary>
    /// Przejście do strony <see cref="Page"/>: na inną pozycję planu — jej efekty (<see cref="LiveItem.Transitions"/>), między slajdami
    /// jednej pozycji — efekty „Slajdy: …” (<see cref="LiveItem.InnerTransitions"/>), poza tym <see cref="SlideChange"/>. Ustawiane przed zmianą <see cref="Page"/>.
    /// </summary>
    [ObservableProperty]
    public partial IPageTransition PageTransition { get; private set; } = SlideChange;

    public string Title => State.Item is { } item ? item.Title : Tr.T("Ekran jest pusty");

    public string? Number => State.Item?.Number?.ToString();

    public bool IsBlank => State.IsBlank;

    public bool HasItem => State.Item is not null;

    public string? NextText => State.Next is { } next ? Tr.F("Dalej: {0}", next) : null;

    public ObservableCollection<SlideTabViewModel> Slides { get; } = [];

    public SlideTabViewModel? CurrentTab =>
        State.Item is not null && State.SlideIndex >= 0 && State.SlideIndex < Slides.Count
            ? Slides[State.SlideIndex]
            : null;

    public SlideTabViewModel? NextTab =>
        State.NextSlide is not null && State.SlideIndex >= 0 && State.SlideIndex + 1 < Slides.Count
            ? Slides[State.SlideIndex + 1]
            : null;

    public IReadOnlyList<SlideLine> CurrentLines => CurrentTab?.Slide.Lines ?? [];

    public IReadOnlyList<SlideLine> NextLines => NextTab?.Slide.Lines ?? [];

    /// <summary>Slajd prezentacji (miniatura) — zamiast wersów.</summary>
    /// <summary>Slajd prezentacji w kolumnie „Na ekranie” — ta sama strona co na rzutniku (także przy czarnym ekranie).</summary>
    public ProjectedImage? CurrentImage => CurrentTab?.Slide is ImageSlide image ? new ProjectedImage(image, _motion) : null;

    public Task<Bitmap?>? NextImage => NextTab?.Slide is ImageSlide image ? SlideImages.LoadThumbnailAsync(image.ImageFile) : null;

    public bool HasCurrentImage => CurrentTab?.Slide is ImageSlide;

    public bool HasNextImage => NextTab?.Slide is ImageSlide;

    public bool HasCurrentVideo => CurrentTab?.Slide is VideoSlide;

    public bool HasCurrentAppWindow => CurrentTab?.Slide is AppWindowSlide;

    /// <summary>Kiosk w kolumnie „Na ekranie” — tak jak na rzutniku, także pod czarnym ekranem (zmiana wtedy stoi).</summary>
    public ProjectedKiosk? CurrentKiosk => CurrentTab?.Slide is KioskSlide kiosk ? new ProjectedKiosk(kiosk, Kiosk) : null;

    public bool HasCurrentKiosk => CurrentTab?.Slide is KioskSlide;

    public string? CurrentLabel => (HasCurrentKiosk ? Kiosk.PositionText : null) ?? CurrentTab?.Label ?? (HasItem && Slides.Count == 0 ? State.Item?.Note ?? Tr.T("Brak slajdów") : null);

    public string? UpcomingLabel => NextTab?.Label ?? NextText;

    public bool HasUpcoming => UpcomingLabel is not null;

    public double ProgressValue => Slides.Count == 0 ? 0 : (State.SlideIndex + 1d) / Slides.Count * 100;

    public string ProgressText => Slides.Count == 0
        ? ""
        : Tr.F("{0} z {1} · {2:0}%", State.SlideIndex + 1, Slides.Count, ProgressValue);

    [RelayCommand]
    private void Next() => _control.Next();

    [RelayCommand]
    private void Previous() => _control.Previous();

    [RelayCommand]
    private void Clear() => _control.Clear();

    [RelayCommand]
    private void GoTo(SlideTabViewModel tab) => _control.GoToSlide(tab.Index);

    /// <summary>Jak slajd z ruchem elementów (<see cref="SlideBuild"/>) wchodzi na ekran — ustalane przy każdej zmianie stanu.</summary>
    private BuildMotion _motion = BuildMotion.Still;

    /// <summary>Kolejny krok budowy slajdu: bez przejścia — ruch zaczyna się od tego, co już widać.</summary>
    private static readonly IPageTransition BuildStep = new CutTransition();

    private void Apply(LiveState state)
    {
        var itemChanged = !ReferenceEquals(State.Item, state.Item);
        var keepCurrentBuild = KeepsCurrentBuild(state);
        if (!keepCurrentBuild)
        {
            var entry = MotionTo(State, state);
            // Stany jednej budowy zmieniają się cięciem, które odsłania nową stronę, gdy ta jest gotowa.
            PageTransition = IsSameBuild(State, state) ? BuildStep : TransitionTo(State, state);
            _motion = entry == BuildEntry.Still ? BuildMotion.Still : new BuildMotion(Play: true, WaitForTransition: true);
        }
        State = state;
        if (itemChanged)
        {
            Slides.Clear();
            foreach (var (slide, index) in (state.Item?.Slides ?? []).Select((s, i) => (s, i)))
                Slides.Add(new SlideTabViewModel(index, slide));
        }
        foreach (var tab in Slides)
            tab.IsCurrent = tab.Index == state.SlideIndex;
        // Następny slajd prezentacji wczytany zawczasu — „Dalej” bez czekania na obraz.
        if (NextTab?.Slide is ImageSlide next)
        {
            SlideImages.Preload(next.ImageFile);
            // warstwy ruchu elementów też zawczasu — „Dalej” rusza od razu
            if (next.Build is { } build)
                _ = SlideBuildImages.LoadAsync(build.Build);
        }

        OnPropertyChanged(nameof(VisibleSlide));
        if (!keepCurrentBuild)
        {
            OnPropertyChanged(nameof(Page));
            OnPropertyChanged(nameof(CurrentImage));
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(IsBlank));
        OnPropertyChanged(nameof(HasItem));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(CurrentTab));
        OnPropertyChanged(nameof(NextTab));
        OnPropertyChanged(nameof(CurrentLines));
        OnPropertyChanged(nameof(NextLines));
        OnPropertyChanged(nameof(NextImage));
        OnPropertyChanged(nameof(HasCurrentImage));
        OnPropertyChanged(nameof(HasNextImage));
        OnPropertyChanged(nameof(HasCurrentVideo));
        OnPropertyChanged(nameof(HasCurrentAppWindow));
        OnPropertyChanged(nameof(CurrentKiosk));
        OnPropertyChanged(nameof(HasCurrentKiosk));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(UpcomingLabel));
        OnPropertyChanged(nameof(HasUpcoming));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressText));
    }

    private bool KeepsCurrentBuild(LiveState state) =>
        state.IsPlaylistRefresh && state.IsWithinItem(State) && state.SlideIndex == State.SlideIndex
        && state.VisibleSlide is ImageSlide { Build: not null } image && image.Equals(State.VisibleSlide as ImageSlide);

    /// <summary>
    /// Ruch elementów gra przy „Dalej” z poprzedniego stanu tego samego slajdu i przy wejściu na slajd z animacjami
    /// automatycznymi (po przejściu); powrót, skok i zdjęcie czarnego ekranu pokazują stan od razu, jak PowerPoint.
    /// </summary>
    private static BuildEntry MotionTo(LiveState before, LiveState after)
    {
        if (after.IsBackwardNavigation || after.VisibleSlide is not ImageSlide { Build: { } frame })
            return BuildEntry.Still;
        if (IsSameBuild(before, after) && before.VisibleSlide is ImageSlide { Build: { } previous })
            return previous.Image == frame.Image - 1 ? BuildEntry.Continue : BuildEntry.Still;
        // Po eksporcie pierwszy slajd staje się widoczny bez zmiany pozycji i indeksu.
        var firstSlideReady = after.IsPlaylistRefresh && before is { IsBlank: false, Item.Slides.Count: 0 };
        if (after.IsWithinItem(before) && after.SlideIndex != before.SlideIndex + 1 && !firstSlideReady)
            return BuildEntry.Still;
        return frame.Image == 0 ? BuildEntry.Enter : BuildEntry.Still;
    }

    /// <summary>Przed i po widać dwa stany tego samego slajdu z ruchem elementów (tej samej budowy).</summary>
    private static bool IsSameBuild(LiveState before, LiveState after) =>
        after.IsWithinItem(before)
        && before.VisibleSlide is ImageSlide { Build: { } previous }
        && after.VisibleSlide is ImageSlide { Build: { } current }
        && previous.Build.Key == current.Build.Key;

    private enum BuildEntry
    {
        /// <summary>Stan od razu (powrót, skok, czarny ekran).</summary>
        Still,

        /// <summary>„Dalej” do następnego stanu tego samego slajdu.</summary>
        Continue,

        /// <summary>Wejście na slajd (animacje automatyczne po przejściu).</summary>
        Enter,
    }

    /// <summary>
    /// Przejście zapisane w prezentacji (PowerPoint: „Przejścia”) między jej slajdami: do przodu — przejście slajdu, na który
    /// się wchodzi; „Wstecz” o jeden slajd — przejście opuszczanego slajdu odegrane wstecz (jak w pokazie DeckCraft / PowerPoint).
    /// </summary>
    private static IPageTransition? FileTransitionTo(LiveState before, LiveState after)
    {
        if (!after.IsWithinItem(before) || before.IsBlank || after.IsBlank || before.SlideIndex == after.SlideIndex)
            return null;
        // „Wstecz” o jeden: tylko przejście opuszczanego slajdu (stan po kliknięciu go nie ma — wtedy jak w planie)
        if (after.SlideIndex == before.SlideIndex - 1)
        {
            if (before.VisibleSlide is not ImageSlide { Transition: { } left })
                return null;
            TransitionLog.Write($"Slajd {before.SlideIndex + 1} → {after.SlideIndex + 1} w „{after.Item?.Title}”: przejście z pliku {left.Kind} wstecz");
            return new FileTransitionPage(left, reverse: true);
        }
        if (after.VisibleSlide is ImageSlide { Transition: { } entered })
        {
            TransitionLog.Write($"Slajd {before.SlideIndex + 1} → {after.SlideIndex + 1} w „{after.Item?.Title}”: przejście z pliku {entered.Kind}");
            return new FileTransitionPage(entered, reverse: false);
        }
        return null;
    }

    private IPageTransition TransitionTo(LiveState before, LiveState after)
    {
        if (FileTransitionTo(before, after) is { } file)
            return file;
        var effects = after.TransitionsFrom(before);
        LogScreenChange(before, after, effects);
        return effects switch
        {
            [] => SlideChange,
            _ when PageTransition is ShaderPageTransition current && current.Effects.SequenceEqual(effects) => current,
            _ => ShaderPageTransition.For(effects),
        };
    }

    /// <summary>Zmiana slajdu, pozycji albo czarnego ekranu — do dziennika przejść z tym, czy dostała efekt, czy krótkie przenikanie.</summary>
    private static void LogScreenChange(LiveState before, LiveState after, IReadOnlyList<SlideTransition> effects)
    {
        var within = after.IsWithinItem(before) && before.IsBlank == after.IsBlank;
        if (within && before.SlideIndex == after.SlideIndex)
            return;
        var blank = before.IsBlank || after.IsBlank ? $" (czarny ekran {before.IsBlank} → {after.IsBlank})" : "";
        var chosen = string.Join(", ", ((within ? after.Item?.InnerTransitions : after.Item?.Transitions) ?? []).Select(SlideTransitions.Name));
        var result = effects.Count == 0 ? $"krótkie przenikanie — {after.NoTransitionsReason(before)}" : within ? "efekt slajdów (pozycji albo planu)" : "efekt planu";
        if (within)
        {
            TransitionLog.Write($"Slajd {before.SlideIndex + 1} → {after.SlideIndex + 1} w „{after.Item?.Title}”; slajdy mają [{chosen}] → {result}");
            return;
        }
        TransitionLog.Write($"Ekran: „{before.Item?.Title}” → „{after.Item?.Title}”{blank}; plan ma [{chosen}] → {result}");
    }
}

/// <summary>
/// To, co rysuje rzutnik. Jeden obiekt (a nie osobne właściwości), żeby tytuł pojawiał się
/// i znikał w tym samym przejściu co tekst slajdu.
/// </summary>
public record ProjectedPage(Slide Slide, string? Title)
{
    public bool HasTitle => Title is not null;
}

/// <summary>Jak slajd z ruchem elementów wchodzi na ekran.</summary>
/// <param name="Play">Odegrać ruch prowadzący do tego slajdu; <c>false</c> = od razu stan po nim.</param>
/// <param name="WaitForTransition">Czy ruch rusza po zakończeniu przejścia w danym widoku.</param>
public sealed record BuildMotion(bool Play, bool WaitForTransition = false)
{
    public static readonly BuildMotion Still = new(Play: false);
}

/// <summary>Slajd prezentacji na rzutniku: obraz na cały ekran (wczytywany w tle) z ruchomymi obrazkami i ruchem elementów, jeśli je ma.</summary>
public sealed record ProjectedImage(ImageSlide Image, BuildMotion Motion) : ProjectedPage(Image, null), IWaitablePage
{
    public ProjectedImage(ImageSlide image)
        : this(image, BuildMotion.Still)
    {
    }

    public Task<Bitmap?> Picture => SlideImages.LoadAsync(Image.ImageFile);

    /// <summary>Animowane GIF-y z prezentacji; pusta lista = zwykły, nieruchomy slajd.</summary>
    public IReadOnlyList<SlideAnimation> Animations => Image.MovingPictures;

    /// <summary>Ruch elementów do odegrania; <c>null</c> = slajd stoi (pokazuje swój obraz).</summary>
    public SlideBuildPlayback? Build => Image.Build is { } frame ? new SlideBuildPlayback(frame, Motion.Play, Motion.WaitForTransition) : null;

    /// <summary>Z ruchem na wejściu czekamy też na warstwy — inaczej przez chwilę byłby widać stan końcowy.</summary>
    public Task Ready => Image.Build is { } frame && Motion.Play
        ? Task.WhenAll(Picture, SlideBuildImages.LoadAsync(frame.Build))
        : Picture;
}

/// <summary>Film na rzutniku: klatki na cały ekran na czarnym tle.</summary>
public sealed record ProjectedVideo(VideoSlide Video, VideoFrames Frames) : ProjectedPage(Video, null);

/// <summary>Aplikacja na rzutniku: obraz okna programu na cały ekran na czarnym tle (bez programu — sama czerń).</summary>
public sealed record ProjectedAppWindow(AppWindowSlide Window, VideoFrames Frames) : ProjectedPage(Window, null);

public sealed partial class SlideTabViewModel(int index, Slide slide) : ObservableObject
{
    public int Index { get; } = index;

    public Slide Slide { get; } = slide;

    public string Label => Slide.Label;

    public string FirstLine => Slide.Lines.Count > 0 ? Slide.Lines[0].Text : "";

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
