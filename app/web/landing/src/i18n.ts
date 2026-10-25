// Język strony startowej: wybrany przełącznikiem (zapamiętany w przeglądarce), inaczej język przeglądarki, gdy jest
// obsługiwany, a w końcu polski. Klucz słownika = tekst polski; brak tłumaczenia = tekst polski.

export type Language = "pl" | "en" | "hu" | "hr";
export const LANGUAGES: Language[] = ["pl", "en", "hu", "hr"];
const KEY = "strona-jezyk";

type Dictionary = Record<string, string>;

const en: Dictionary = {
  "Pobierz": "Download",
  " dla Windows": " for Windows",
  "Język": "Language",
  "Pieśni na rzutniku, prowadzone z jednego miejsca": "Songs on the projector, run from one place",
  "Śpiewnik z akordami, plan nabożeństwa, prezentacje i filmy w programie na komputerze przy rzutniku. Telefon muzyka i drugi komputer łączą się z nim przez tę stronę.":
    "Songbook with chords, service plan, presentations and videos in a program on the computer by the projector. A musician's phone and a second computer connect to it through this page.",
  "Tak wygląda pieśń na rzutniku": "This is how a song looks on the projector",
  "Połącz się z programem": "Connect to the program",
  "Pilot w telefonie": "Remote on your phone",
  "Bieżący i następny slajd z akordami, Dalej i Wstecz pod kciukiem.": "Current and next slide with chords, Next and Back under your thumb.",
  "Panel na komputerze lub tablecie": "Panel on a computer or tablet",
  "Plany, kolejność pieśni, układ zwrotek i to, co widzi sala.": "Plans, song order, verse arrangement and what the room sees.",
  "Pierwsze połączenie: w programie otwórz „Urządzenia” i zeskanuj kod albo wpisz krótki kod. Potem łączy się samo.":
    "First connection: in the program, open “Devices” and scan the code or type the short code. After that it connects by itself.",
  "Wersja dla Linuksa i poprzednie wydania": "Linux version and previous releases",
};

const hu: Dictionary = {
  "Pobierz": "Letöltés",
  " dla Windows": " Windowsra",
  "Język": "Nyelv",
  "Pieśni na rzutniku, prowadzone z jednego miejsca": "Énekek a kivetítőn, egy helyről irányítva",
  "Śpiewnik z akordami, plan nabożeństwa, prezentacje i filmy w programie na komputerze przy rzutniku. Telefon muzyka i drugi komputer łączą się z nim przez tę stronę.":
    "Énekeskönyv akkordokkal, istentiszteleti terv, bemutatók és videók a kivetítő melletti számítógép programjában. A zenész telefonja és egy második számítógép ezen az oldalon keresztül kapcsolódik hozzá.",
  "Tak wygląda pieśń na rzutniku": "Így néz ki egy ének a kivetítőn",
  "Połącz się z programem": "Kapcsolódás a programhoz",
  "Pilot w telefonie": "Távirányító a telefonon",
  "Bieżący i następny slajd z akordami, Dalej i Wstecz pod kciukiem.": "Az aktuális és a következő dia akkordokkal, Tovább és Vissza kézügyben.",
  "Panel na komputerze lub tablecie": "Panel számítógépen vagy táblagépen",
  "Plany, kolejność pieśni, układ zwrotek i to, co widzi sala.": "Tervek, az énekek sorrendje, a versszakok rendje és az, amit a terem lát.",
  "Pierwsze połączenie: w programie otwórz „Urządzenia” i zeskanuj kod albo wpisz krótki kod. Potem łączy się samo.":
    "Első kapcsolódás: a programban nyisd meg az „Eszközök” ablakot, és olvasd be a kódot, vagy írd be a rövid kódot. Utána magától kapcsolódik.",
  "Wersja dla Linuksa i poprzednie wydania": "Linux-verzió és korábbi kiadások",
};

const hr: Dictionary = {
  "Pobierz": "Preuzmi",
  " dla Windows": " za Windows",
  "Język": "Jezik",
  "Pieśni na rzutniku, prowadzone z jednego miejsca": "Pjesme na projektoru, vođene s jednog mjesta",
  "Śpiewnik z akordami, plan nabożeństwa, prezentacje i filmy w programie na komputerze przy rzutniku. Telefon muzyka i drugi komputer łączą się z nim przez tę stronę.":
    "Pjesmarica s akordima, plan bogoslužja, prezentacije i videozapisi u programu na računalu uz projektor. Glazbenikov telefon i drugo računalo povezuju se s njim preko ove stranice.",
  "Tak wygląda pieśń na rzutniku": "Ovako pjesma izgleda na projektoru",
  "Połącz się z programem": "Poveži se s programom",
  "Pilot w telefonie": "Daljinski na telefonu",
  "Bieżący i następny slajd z akordami, Dalej i Wstecz pod kciukiem.": "Trenutni i sljedeći slajd s akordima, Dalje i Natrag pod palcem.",
  "Panel na komputerze lub tablecie": "Panel na računalu ili tabletu",
  "Plany, kolejność pieśni, układ zwrotek i to, co widzi sala.": "Planovi, redoslijed pjesama, raspored kitica i ono što dvorana vidi.",
  "Pierwsze połączenie: w programie otwórz „Urządzenia” i zeskanuj kod albo wpisz krótki kod. Potem łączy się samo.":
    "Prvo povezivanje: u programu otvori „Uređaji” i skeniraj kôd ili upiši kratki kôd. Nakon toga povezuje se samo.",
  "Wersja dla Linuksa i poprzednie wydania": "Verzija za Linux i prethodna izdanja",
};

const dictionaries: Record<Language, Dictionary | null> = { pl: null, en, hu, hr };

const supported = (value: string | null | undefined): Language | null => {
  const code = value?.slice(0, 2).toLowerCase();
  return LANGUAGES.find((l) => l === code) ?? null;
};

export const initialLanguage = (): Language =>
  supported(localStorage.getItem(KEY)) ?? supported(navigator.language) ?? "pl";

export const rememberLanguage = (language: Language) => localStorage.setItem(KEY, language);

/** Tłumacz dla wybranego języka; klucz = tekst polski. */
export const translator = (language: Language) => (text: string) => dictionaries[language]?.[text] ?? text;
