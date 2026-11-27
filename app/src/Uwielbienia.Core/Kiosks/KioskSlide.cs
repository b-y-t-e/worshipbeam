using Uwielbienia.Core.Localization;
using Uwielbienia.Core.Plans;
using Uwielbienia.Core.Presentation;

namespace Uwielbienia.Core.Kiosks;

/// <summary>
/// Kiosk na ekranie: jeden slajd, w którym elementy (<see cref="Frames"/>) zmieniają się same co <see cref="Interval"/>
/// (<see cref="KioskRotation"/>). „Dalej” przechodzi do następnej pozycji planu.
/// </summary>
/// <param name="Transitions">Efekty przejścia między elementami (losowany jeden); pusta = przenikanie.</param>
/// <param name="SlowZoom">Zdjęcia powoli przybliżają się i przesuwają.</param>
public sealed record KioskSlide(
    IReadOnlyList<KioskFrame> Frames,
    TimeSpan Interval,
    IReadOnlyList<SlideTransition>? Transitions = null,
    bool SlowZoom = false) : Slide("K", Tr.T("Kiosk"), [])
{
    public IReadOnlyList<SlideTransition> ChosenTransitions => Transitions ?? [];

    /// <summary>Jak długo widoczny jest element o tym numerze: jego własny czas albo czas kiosku.</summary>
    public TimeSpan Duration(int index) =>
        index >= 0 && index < Frames.Count && Frames[index].Seconds is { } seconds
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, KioskPlanItem.MinSeconds, KioskPlanItem.MaxSeconds))
            : Interval;

    /// <summary>
    /// Jak długo zostaje na ekranie ten sam obraz — razem z kolejnymi napisami tego samego elementu (zoom zdjęcia trwa
    /// przez nie wszystkie). Liczone od tego elementu do końca ciągu klatek z tym samym obrazem.
    /// </summary>
    public TimeSpan RunDuration(int index)
    {
        if (index < 0 || index >= Frames.Count)
            return Interval;
        var total = Duration(index);
        if (Frames[index].Image is not { } image)
            return total;
        for (var i = index + 1; i < Frames.Count && Frames[i].Image == image; i++)
            total += Duration(i);
        return total;
    }

    /// <summary>
    /// Czy przejście z jednej klatki na drugą zostaje w tym samym ciągu zdjęcia (następna klatka, ten sam obraz) —
    /// wtedy zdjęcie zostaje na ekranie, a zmienia się sam napis. Powrót na początek ciągu to nowe okrążenie: zdjęcie
    /// pokazuje się od nowa, więc powolny zoom rusza jeszcze raz.
    /// </summary>
    public bool ContinuesRun(int from, int to) =>
        to == from + 1 && from >= 0 && to < Frames.Count && Frames[from].Image is { } image && image == Frames[to].Image;

    // Porównanie treści (a nie odwołania do listy): przebudowany plan z tym samym kioskiem to ten sam slajd —
    // ekran nie przenika i rotacja nie zaczyna od nowa (LiveSession.SetPlaylist).
    public bool Equals(KioskSlide? other) =>
        other is not null && base.Equals(other) && Interval == other.Interval && SlowZoom == other.SlowZoom
        && Frames.SequenceEqual(other.Frames) && ChosenTransitions.SequenceEqual(other.ChosenTransitions);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Interval, Frames.Count);
}

/// <summary>Jak element kiosku dzieli ekran.</summary>
public enum KioskLayout
{
    /// <summary>Sam obraz na cały ekran.</summary>
    Image,
    /// <summary>Sam napis (tytuł i tekst) na środku.</summary>
    Text,
    /// <summary>Obraz z lewej, napis z prawej — obraz pionowy lub zbliżony do kwadratu.</summary>
    TextRight,
    /// <summary>Obraz u góry, napis pod nim — obraz poziomy.</summary>
    TextBelow,

    /// <summary>Obraz na cały ekran, napis na nim (u góry, na środku albo u dołu).</summary>
    TextOver,
}

/// <summary>Jeden element kiosku na ekranie.</summary>
/// <param name="Image">Pełna ścieżka obrazu; <c>null</c> = sam napis.</param>
/// <param name="Text">Treść napisu; wiersze rozdziela <c>\n</c>.</param>
/// <param name="Style">Wygląd napisu (<see cref="KioskEntry.Style"/>); <c>null</c> = domyślny.</param>
/// <param name="Seconds">Czas tego elementu; <c>null</c> = czas kiosku.</param>
public sealed record KioskFrame(string? Image, string? Title, string? Text, KioskCaptionStyle? Style = null, int? Seconds = null)
{
    /// <summary>Płótno rzutnika i miejsce na napis obok obrazu (z prawej) albo pod nim — w pikselach płótna.</summary>
    public const double ScreenWidth = 1920, ScreenHeight = 1080, SideTextWidth = 640, BottomTextHeight = 300;

    public bool HasText => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Text);

    public KioskCaptionStyle ChosenStyle => Style ?? KioskCaptionStyle.Default;

    /// <summary>
    /// Napis na obrazie, gdy tak wybrano (<see cref="KioskCaptionPlacement.Over"/>). Poza tym tam, gdzie obraz zostaje
    /// największy: obraz pionowy, kwadratowy albo do ok. 3:2 — napis z prawej; szerszy (np. 16:9) — pod spodem.
    /// </summary>
    /// <param name="imageAspect">Szerokość / wysokość obrazu.</param>
    public KioskLayout LayoutFor(double imageAspect)
    {
        if (Image is null)
            return KioskLayout.Text;
        if (!HasText)
            return KioskLayout.Image;
        if (ChosenStyle.Placement == KioskCaptionPlacement.Over)
            return KioskLayout.TextOver;
        if (!double.IsFinite(imageAspect) || imageAspect <= 0)
            return KioskLayout.TextBelow;
        var right = FittedArea(ScreenWidth - SideTextWidth, ScreenHeight, imageAspect);
        var below = FittedArea(ScreenWidth, ScreenHeight - BottomTextHeight, imageAspect);
        return right > below ? KioskLayout.TextRight : KioskLayout.TextBelow;
    }

    private static double FittedArea(double width, double height, double aspect)
    {
        var w = Math.Min(width, height * aspect);
        return w * (w / aspect);
    }
}
