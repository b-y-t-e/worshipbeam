// Język pilota: zawsze ten, który wybrano w aplikacji (pole `language` w stanie). Do pierwszego stanu — ostatni
// zapamiętany w tej przeglądarce, potem język przeglądarki, gdy jest obsługiwany, a w końcu polski.
// Klucz słownika = tekst polski; brak tłumaczenia = tekst polski. `{0}`, `{1}` — wartości wstawiane.
// Teksty w HTML: data-t (treść), data-t-aria (aria-label), data-t-title, data-t-placeholder — wartość = tekst polski.

const LANGUAGES = ["pl", "en", "hu", "hr"];
const KEY = "jezyk";

const en = {
  "WorshipBeam — pilot": "WorshipBeam — remote",
  "Na komputerze otwórz „Urządzenia” i zeskanuj kod aparatem.": "On the computer, open “Devices” and scan the code with your camera.",
  "Kod parowania": "Pairing code",
  "np. ajk59": "e.g. ajk59",
  "Połącz": "Connect",
  "Ekran jest pusty": "The screen is empty",
  "Łączenie…": "Connecting…",
  "Łączenie ponownie…": "Reconnecting…",
  "Połączono": "Connected",
  "Brak połączenia": "No connection",
  "Polecenie nie dotarło": "Command didn't arrive",
  "Dodaj pieśń jako następną": "Add a song as next",
  "Akordy": "Chords",
  "Jasny wygląd": "Light theme",
  "Ciemny wygląd": "Dark theme",
  "Ekran": "Screen",
  "Odłącz od aplikacji": "Disconnect from the app",
  "Ekran wygaszony": "Screen blanked",
  "Pauza": "Pause",
  "Odtwórz": "Play",
  "Obraz": "Picture",
  "Obraz wcześniej o 0,1 s": "Picture 0.1 s earlier",
  "Obraz później o 0,1 s": "Picture 0.1 s later",
  "Wstecz": "Back",
  "Dalej": "Next",
  "Numer albo słowa pieśni": "Song number or words",
  "Anuluj": "Cancel",
  "Nic nie znaleziono.": "Nothing found.",
  "Cofnij": "Undo",
  "Wróć do pilota": "Back to remote",
  "Kod wygasł albo został już użyty. Poproś o nowy w aplikacji.": "The code has expired or was already used. Get a new one in the app.",
  "Nie udało się użyć kodu. Poproś o nowy w aplikacji.": "The code couldn't be used. Get a new one in the app.",
  "Aplikacja nie zna już tego urządzenia. Zeskanuj nowy kod.": "The app no longer knows this device. Scan a new code.",
  "Kiosk · {0} z {1}": "Kiosk · {0} of {1}",
  " · zatrzymany": " · paused",
  "Dalej: {0}": "Next: {0}",
  "Nie udało się dodać.": "Couldn't add.",
  "Następna: {0}": "Next: {0}",
  "Cofnięto.": "Undone.",
  "Nie udało się cofnąć.": "Couldn't undo.",
  "Odłączyć od aplikacji? Telefon przestanie zmieniać slajdy, a żeby wrócić, potrzebny będzie nowy kod.":
    "Disconnect from the app? The phone will stop changing slides, and you will need a new code to come back.",
};

const hu = {
  "WorshipBeam — pilot": "WorshipBeam — távirányító",
  "Na komputerze otwórz „Urządzenia” i zeskanuj kod aparatem.": "A számítógépen nyisd meg az „Eszközök” ablakot, és olvasd be a kódot a kamerával.",
  "Kod parowania": "Párosítási kód",
  "np. ajk59": "pl. ajk59",
  "Połącz": "Kapcsolódás",
  "Ekran jest pusty": "A kivetítő üres",
  "Łączenie…": "Kapcsolódás…",
  "Łączenie ponownie…": "Újrakapcsolódás…",
  "Połączono": "Kapcsolódva",
  "Brak połączenia": "Nincs kapcsolat",
  "Polecenie nie dotarło": "A parancs nem érkezett meg",
  "Dodaj pieśń jako następną": "Ének hozzáadása következőként",
  "Akordy": "Akkordok",
  "Jasny wygląd": "Világos megjelenés",
  "Ciemny wygląd": "Sötét megjelenés",
  "Ekran": "Kivetítő",
  "Odłącz od aplikacji": "Leválasztás az alkalmazásról",
  "Ekran wygaszony": "Kivetítő elsötétítve",
  "Pauza": "Szünet",
  "Odtwórz": "Lejátszás",
  "Obraz": "Kép",
  "Obraz wcześniej o 0,1 s": "Kép 0,1 mp-cel korábban",
  "Obraz później o 0,1 s": "Kép 0,1 mp-cel később",
  "Wstecz": "Vissza",
  "Dalej": "Tovább",
  "Numer albo słowa pieśni": "Az ének száma vagy szavai",
  "Anuluj": "Mégse",
  "Nic nie znaleziono.": "Nincs találat.",
  "Cofnij": "Visszavonás",
  "Wróć do pilota": "Vissza a távirányítóhoz",
  "Kod wygasł albo został już użyty. Poproś o nowy w aplikacji.": "A kód lejárt vagy már felhasználták. Kérj újat az alkalmazásban.",
  "Nie udało się użyć kodu. Poproś o nowy w aplikacji.": "A kódot nem sikerült használni. Kérj újat az alkalmazásban.",
  "Aplikacja nie zna już tego urządzenia. Zeskanuj nowy kod.": "Az alkalmazás már nem ismeri ezt az eszközt. Olvass be új kódot.",
  "Kiosk · {0} z {1}": "Kioszk · {0} / {1}",
  " · zatrzymany": " · megállítva",
  "Dalej: {0}": "Következik: {0}",
  "Nie udało się dodać.": "Nem sikerült hozzáadni.",
  "Następna: {0}": "Következő: {0}",
  "Cofnięto.": "Visszavonva.",
  "Nie udało się cofnąć.": "Nem sikerült visszavonni.",
  "Odłączyć od aplikacji? Telefon przestanie zmieniać slajdy, a żeby wrócić, potrzebny będzie nowy kod.":
    "Leválasztod az alkalmazásról? A telefon nem vált többé diát, a visszatéréshez új kód kell.",
};

const hr = {
  "WorshipBeam — pilot": "WorshipBeam — daljinski",
  "Na komputerze otwórz „Urządzenia” i zeskanuj kod aparatem.": "Na računalu otvori „Uređaji” i skeniraj kôd kamerom.",
  "Kod parowania": "Kôd za uparivanje",
  "np. ajk59": "npr. ajk59",
  "Połącz": "Poveži",
  "Ekran jest pusty": "Zaslon je prazan",
  "Łączenie…": "Povezivanje…",
  "Łączenie ponownie…": "Ponovno povezivanje…",
  "Połączono": "Povezano",
  "Brak połączenia": "Nema veze",
  "Polecenie nie dotarło": "Naredba nije stigla",
  "Dodaj pieśń jako następną": "Dodaj pjesmu kao sljedeću",
  "Akordy": "Akordi",
  "Jasny wygląd": "Svijetli izgled",
  "Ciemny wygląd": "Tamni izgled",
  "Ekran": "Zaslon",
  "Odłącz od aplikacji": "Odspoji od aplikacije",
  "Ekran wygaszony": "Zaslon zatamnjen",
  "Pauza": "Pauza",
  "Odtwórz": "Reproduciraj",
  "Obraz": "Slika",
  "Obraz wcześniej o 0,1 s": "Slika 0,1 s ranije",
  "Obraz później o 0,1 s": "Slika 0,1 s kasnije",
  "Wstecz": "Natrag",
  "Dalej": "Dalje",
  "Numer albo słowa pieśni": "Broj ili riječi pjesme",
  "Anuluj": "Odustani",
  "Nic nie znaleziono.": "Ništa nije pronađeno.",
  "Cofnij": "Poništi",
  "Wróć do pilota": "Natrag na daljinski",
  "Kod wygasł albo został już użyty. Poproś o nowy w aplikacji.": "Kôd je istekao ili je već iskorišten. Zatraži novi u aplikaciji.",
  "Nie udało się użyć kodu. Poproś o nowy w aplikacji.": "Kôd nije moguće iskoristiti. Zatraži novi u aplikaciji.",
  "Aplikacja nie zna już tego urządzenia. Zeskanuj nowy kod.": "Aplikacija više ne poznaje ovaj uređaj. Skeniraj novi kôd.",
  "Kiosk · {0} z {1}": "Kiosk · {0} od {1}",
  " · zatrzymany": " · zaustavljeno",
  "Dalej: {0}": "Slijedi: {0}",
  "Nie udało się dodać.": "Dodavanje nije uspjelo.",
  "Następna: {0}": "Sljedeća: {0}",
  "Cofnięto.": "Poništeno.",
  "Nie udało się cofnąć.": "Poništavanje nije uspjelo.",
  "Odłączyć od aplikacji? Telefon przestanie zmieniać slajdy, a żeby wrócić, potrzebny będzie nowy kod.":
    "Odspojiti od aplikacije? Telefon više neće mijenjati slajdove, a za povratak trebat će novi kôd.",
};

const dictionaries = { pl: null, en, hu, hr };

const supported = (value) => {
  const code = value?.slice(0, 2).toLowerCase();
  return LANGUAGES.includes(code) ? code : null;
};

let language = supported(localStorage.getItem(KEY)) ?? supported(navigator.language) ?? "pl";
const listeners = [];

export const getLanguage = () => language;

/** Tekst w bieżącym języku; klucz = tekst polski, `{0}`… = kolejne wartości. */
export function t(text, ...values) {
  const translated = dictionaries[language]?.[text] ?? text;
  return translated.replace(/\{(\d+)\}/g, (m, i) => (values[+i] !== undefined ? String(values[+i]) : m));
}

/** Teksty stałe z HTML (atrybuty data-t…) w bieżącym języku. */
export function translatePage() {
  document.documentElement.lang = language;
  for (const el of document.querySelectorAll("[data-t]")) el.textContent = t(el.dataset.t);
  for (const el of document.querySelectorAll("[data-t-aria]")) el.setAttribute("aria-label", t(el.dataset.tAria));
  for (const el of document.querySelectorAll("[data-t-title]")) el.title = t(el.dataset.tTitle);
  for (const el of document.querySelectorAll("[data-t-placeholder]")) el.placeholder = t(el.dataset.tPlaceholder);
  if (document.body.dataset.tDocTitle) document.title = t(document.body.dataset.tDocTitle);
}

/** Język z aplikacji (stan); nieznany albo brak — bez zmiany. */
export function setLanguage(value) {
  const next = supported(value);
  if (!next || next === language) return;
  language = next;
  localStorage.setItem(KEY, next);
  translatePage();
  listeners.forEach((l) => l());
}

/** Po zmianie języka — np. ponowne rysowanie tekstów ustawianych w kodzie. */
export const onLanguageChange = (listener) => listeners.push(listener);
