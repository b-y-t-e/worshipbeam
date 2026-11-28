# Animacje DeckCraft — port z Rust

Silnik czasu animacji (kroki, efekty, stan kształtów w czasie) przeniesiony z
[DeckCraft](https://github.com/storytold/deckcraft) (MIT OR Apache-2.0, licencja MIT: `DECKCRAFT-LICENSE.txt`).

## Cel

**Docelowo 100% efektów animacji i przejść slajdów DeckCraft, wyglądających tak samo jak w DeckCraft** (a przez to jak
w PowerPoincie, do którego DeckCraft jest mierzony). Kod przenosimy 1:1 i każdą wartość porównujemy z oryginałem w Rust,
a przy każdej nowej wersji DeckCraft przenosimy różnicę. Efekt, którego WorshipBeam nie umie pokazać w pełni (bo pracuje
na warstwach z DeckCraft, a nie rysuje kształtów sam), jest wypisany w `CLAUDE.md` jako ograniczenie, a nie po cichu pominięty.

## Stan synchronizacji

Aktualizuje go `python tools/deckcraft_sync.py` przy **każdym** uruchomieniu (sprawdzenie) i przy `--apply` (przeniesienie).

- **Repozytorium:** https://github.com/storytold/deckcraft (gałąź `main`)
- **Wersja:** `f67d07f132609482297a8272a8620e197262a51e` (2026-11-27) — przeniesiona: z niej są kod C# i wzorce testów
  (animacje i przejścia — `UPSTREAM-TRANSITIONS.md` ma tę samą)
- **Ostatnio sprawdzone:** `f67d07f132609482297a8272a8620e197262a51e` (2026-11-27), sprawdzono 2026-11-28 — bez zmian w przenoszonych plikach
- Różnice następnym razem liczą się od ostatnio sprawdzonej wersji, jeśli była bez zmian, inaczej od przeniesionej.

| Sprawdzono | Commit DeckCraft | Wynik |
|---|---|---|
| 2026-11-28 | `f67d07f13260` (2026-11-27) | port: animacje (wszystkie efekty galerii) i przejścia (wszystkie 48) |
| 2026-11-28 | `f67d07f13260` (2026-11-27) | bez zmian w przenoszonych plikach |

- Jeden plik C# na moduł Rust, te same identyfikatory efektów i nazwy funkcji (PascalCase), żeby zmiany w oryginale
  przenosiło się 1:1. Każdy plik ma w pierwszej linii nazwę pliku Rust, który odwzorowuje.

| Plik C# | Rust |
|---|---|
| `Easing.cs` | `crates/anim/src/easing.rs` |
| `MotionPath.cs` | `crates/anim/src/path.rs` (+ `preset_path` z `crates/model/src/anim.rs`) |
| `Effects.cs` | `crates/anim/src/effects.rs` |
| `Timeline.cs` | `crates/anim/src/timeline.rs` |
| `AnimState.cs` | `crates/anim/src/lib.rs` (`AnimState`, `clampf`), `Rgba` z craft-drawingml (`lerp`, `from_hex`) |
| `AnimationModel.cs` | `crates/model/src/anim.rs` (`Animation`, `AnimClass`, `AnimStart`, `TextBuild`, `BuildRole`, `paragraph_build_roles`, `MAX_BUILD_PARAGRAPHS`) |

Przejścia slajdów (`transition*`, `mesh.rs`) są przeniesione osobno — `UPSTREAM-TRANSITIONS.md`. Nie przeniesione: morph,
interact, show.

Różnice względem Rust:

- Kształty przychodzą jako `TimelineShape` (pole, kolor wypełnienia jako `#RRGGBB`, akapity), nie jako model slajdu;
  kolory animacji (`Animation.Color`) — już rozwiązane. Kolory motywu w `Animation.FromJson` rozwiązuje domyślny schemat
  DeckCraft (jak `Timeline::new`); przekształcenia koloru (`mods`: lumMod, tint…) i kolory nazwane (`preset`) są pomijane.
- Bez `xfrm_of` (`Timeline::with`): pole kształtu podaje wywołujący.

## Ograniczenia pokazu

- Skalowanie i obrót kształtu w trakcie animacji przekształcają gotową warstwę rastrową. Wynik jest przybliżeniem
  renderu DeckCraft także w stanie końcowym, np. po trwałym powiększeniu kształtu z cienkim obrysem.
- Oś czasu uwzględnia polecenia multimedialne. Osadzony film pozostaje nieruchomą warstwą także po kroku `Play`.
- Eksport zapisuje do trzech wariantów koloru wyróżnienia na kształt. Czwarty niezależny kolor i kolejne mogą być
  przybliżone przez przenikanie między kolorem wyjściowym a jednym z zapisanych wariantów.

## Wyniki oryginału (fixtures)

`tools/deckcraft-anim-fixtures` (Cargo, zależy od `deckcraft-anim` i `deckcraft-model` z gita, ta sama wersja) zapisuje
w `app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftAnim/`:

- `effects.json` — każdy efekt z galerii (`ANIMATIONS`) × każda opcja (i pusta): stan co 0,05 s,
- `timelines.json` — scenariusze (starty, opóźnienia, powtórzenia, do kliknięcia, autoReverse, rewind, `after`, budowanie
  akapitami, wyzwalacze, wygładzanie, ścieżki, kolory, multimedia): kroki, czasy efektów, stany.

`DeckCraftAnimTests` porównuje każdą wartość (tolerancja 1e-6, kolory dokładnie). Test
`Every_gallery_effect_is_ported_or_listed_as_unsupported` nie przejdzie, gdy w oryginale pojawi się nowy efekt.

Ręcznie (Windows: linker MSVC musi widzieć biblioteki — `LIB` z Visual Studio / Build Tools):

```
cargo run --release --ignore-rust-version --manifest-path tools/deckcraft-anim-fixtures/Cargo.toml
dotnet test app --filter DeckCraftAnim
```

## Aktualizacja

```
python tools/deckcraft_sync.py                 # co się zmieniło od tej wersji do origin/main
python tools/deckcraft_sync.py --to <rev>      # … do wskazanej wersji
python tools/deckcraft_sync.py --apply         # odtwarza wzorce z nowej wersji i uruchamia testy; wersja przestawia się tylko, gdy przejdą
```

Kolejność: `python tools/deckcraft_sync.py` (różnica), `--apply` (odtwarza wzorce z nowej wersji i uruchamia testy; gdy nie przejdą, przeniesiona wersja zostaje stara), przeniesienie różnicy do plików C#, aż testy przejdą, i `--apply` jeszcze raz (dopiero wtedy wersja się przestawia).

Narzędzie pobiera DeckCraft do katalogu podręcznego (`%TEMP%/deckcraft-sync`, `--repo` = własna kopia), wypisuje
`git diff --stat` i pełną różnicę `crates/anim/src` (animacje i przejścia; bez testów, morph, interact, show),
`crates/model/src/anim.rs`, `crates/ui-egui/src/show.rs` (wybór przejścia przy cofaniu), `crates/ui-egui/src/show/ui.rs`
i `crates/ui-egui/src/show/tests.rs` (odtwarzanie przejść w pokazie i jego testy).
Wypisuje też efekty i przejścia dodane i usunięte w tabelach `ANIMATIONS` i `TRANSITIONS`.
Zmiany przenosi się do plików C# o tych samych nazwach, aż `dotnet test app --filter DeckCraft` przejdzie.
