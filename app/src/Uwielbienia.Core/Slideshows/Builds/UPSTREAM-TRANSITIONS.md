# Przejścia slajdów z DeckCraft — upstream

Port geometrii przejść z [DeckCraft](https://github.com/storytold/deckcraft) (MIT OR Apache-2.0),
rewizja `f67d07f132609482297a8272a8620e197262a51e` (2026-11-27). Licencja: `DECKCRAFT-LICENSE.txt`
(port animacji, ten sam folder).

| C# (`Slideshows/Builds`) | Rust |
|---|---|
| `Transition.cs` (`TransitionSource`, `TransitionLayer`, `SlideTransitionFrames.Frame` / `UsesSlideParts` / `DefaultAspect`) | `crates/anim/src/transition.rs` |
| `TransitionFx.cs` | `crates/anim/src/transition_fx.rs` |
| `TransitionGeom.cs` | `crates/anim/src/transition_geom.rs`, `crates/anim/src/lib.rs` (`clampf`) |
| `Easing.cs` (`Ease`, `Smooth`, wspólne dla animacji i przejść) | `crates/anim/src/easing.rs` (`ease`, `smooth`) |
| `TransitionMesh.cs` | `crates/anim/src/mesh.rs` |
| `TransitionCatalog.cs` | `crates/model/src/anim.rs` (`TRANSITIONS`, `transition_option_label`) |
| `FileTransitions.cs` | craft-libs `crates/pptx/src/read/timing.rs` (`transition`), `maps.rs` (`transition_from_xml`), craft-drawingml `xmlx.rs` (`resolve_mc`) — rev `8fa4db64a9bb943520259ea95331a303ba788f21` |
| `SlideTransitionFrames.ReverseFrame`, `TransitionSources.SwapOldNew` | `crates/ui-egui/src/show/ui.rs` (`transition_layers(.., reverse, ..)`, `swap_old_new`) |

Te same identyfikatory, nazwy i kolejność gałęzi co w Ruście, żeby diff upstreamu przekładał się 1:1.

## Powrót do poprzedniego slajdu (#1531)

DeckCraft (jak PowerPoint) przy cofnięciu z B do A odtwarza **przejście slajdu B** (tego, który opuszczamy),
a nie przejście A: `ShowState.backward`, w UI `reverse = back && kind != "morph"`. Klatka:
`transition_frame(kind, option, 1 − p, aspect)` z zamienionymi źródłami Old ↔ New (tło i zawartość też).
W porcie: `SlideTransitionFrames.ReverseFrame(kind, option, p, aspect)` — `Old` = slajd opuszczany (B),
`New` = slajd, do którego wracamy (A), `p` biegnie 0 → 1. DeckCraft odtwarza Morph przy powrocie w zwykłym kierunku.

## Co nie jest 1:1

- Morph w WorshipBeam odtwarza się jako przenikanie całych slajdów. Wzorce obejmują to przenikanie z `transition_frame`.
  DeckCraft w pokazie używa dodatkowo `morph_frame` do animowania dopasowanych kształtów.
- `FileTransitions`: dźwięk przejścia liczy się jako obecny, gdy `p:sndAc/p:stSnd/p:snd` ma `r:embed`
  (Rust sprawdza, czy osadzony plik da się wczytać) — ma to znaczenie tylko dla decyzji, czy `none` bez efektu
  zwrócić jako `null`. Pomijamy `keep` z `resolve_mc_keeping` (dotyczy tylko modeli 3-D).
- Czytnik zwraca `none` (a nie `null`), gdy slajd nie ma efektu, ale ma `advTm`, `advClick="0"` albo dźwięk — jak Rust.
- `TransitionMesh.LayerMesh(layer, out triangles)` zwraca indeksy płasko (po 3 na trójkąt) zamiast `[u32; 3]`.

## Fixtures

`app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftTransitions/` (testy: `DeckCraftTransitionTests`):

- `frames.json` — `transition_frame` dla każdego id × opcja (i pusta opcja) przy t = 0, 0.1, 0.25, 0.5, 0.75, 0.9, 1,
  16:9 i (rodzaje zależne od proporcji) 4:3; `uses_slide_parts`; `layer_mesh` wszystkich warstw kilku klatek;
  tabela `TRANSITIONS` z etykietami opcji. Klatki do 48 warstw (i najmniejsza pośrednia klatka pierwszej opcji
  każdego rodzaju) są zapisane warstwa po warstwie; gęstsze (cząstki, płytki, pola) — liczba warstw, źródła
  i suma kontrolna wszystkich liczb (inaczej plik miałby ~84 MB).
- `pptx/*.pptx` + `pptx.json` — przejścia przeczytane z tych plików czytnikiem PPTX DeckCraft
  (`deckcraft_pptx::import`), jeden wpis na slajd. `kinds-N.pptx` zapisał DeckCraft (wszystkie rodzaje × opcje),
  `handwritten.pptx` ma ręcznie wpisane `p:transition` (AlternateContent p14/p15 z Fallback, nieznane `Requires`,
  `spd`, `p14:dur`, `prstTrans`, nieznany efekt, slajd ukryty).

Odtworzenie wzorców dla zapisanej rewizji (z katalogu repozytorium):

```
python -I tools/deckcraft-transition-fixtures/make_decks.py      # tylko gdy zmieniają się prezentacje; deckcraft-cli 0.19.1
cd tools/deckcraft-transition-fixtures
cargo run --release --ignore-rust-version                        # pisze frames.json i pptx.json
cd ../.. && dotnet test app --filter DeckCraftTransition
```

`--ignore-rust-version`: craft-libs wymaga rustc 1.95, a kod buduje się też na 1.94. Na tym komputerze linker
MSVC potrzebuje ustawionego `LIB` (MSVC `lib\x64` + `lib\onecore\x64`, Windows Kits `um\x64` i `ucrt\x64`).

Nową rewizję DeckCraft sprawdzamy i przenosimy wspólnie dla animacji i przejść przez `python tools/deckcraft_sync.py`
zgodnie z [procedurą aktualizacji w UPSTREAM.md](UPSTREAM.md#aktualizacja). Wpis „Ostatnio sprawdzone” i historia
sprawdzeń są w [sekcji „Stan synchronizacji” w UPSTREAM.md](UPSTREAM.md#stan-synchronizacji). Opcja `--apply`
odtwarza wzorce i uruchamia testy. Po ich przejściu nowa rewizja zostaje w obu notatkach i manifestach generatorów.
Test `Every_fixture_kind_is_in_the_catalog` pada, gdy DeckCraft doda przejście.
