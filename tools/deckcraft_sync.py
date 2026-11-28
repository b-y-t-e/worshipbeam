"""Synchronizacja portu DeckCraft — animacji i przejść (app/src/Uwielbienia.Core/Slideshows/Builds, opis w UPSTREAM.md
i UPSTREAM-TRANSITIONS.md).

Użycie: python tools/deckcraft_sync.py [--to REV] [--repo PATH] [--apply]

Pokazuje zmiany w oryginale (Rust) od wersji zapisanej w UPSTREAM.md do REV (domyślnie origin/main): git diff --stat
i pełną różnicę crates/anim/src (bez testów, morph, interact, show), crates/model/src/anim.rs, crates/pptx/src/convert.rs,
crates/ui-egui/src/show.rs oraz plików ui.rs i tests.rs w crates/ui-egui/src/show.
Wypisuje też efekty i przejścia dodane / usunięte w tabelach ANIMATIONS i TRANSITIONS.
Z --apply przestawia wersję w obu plikach UPSTREAM
i w Cargo.toml obu programów wzorców (tools/deckcraft-*-fixtures), odtwarza wzorce (cargo run --release) i uruchamia
dotnet test app --filter DeckCraft. Gdy coś nie przejdzie, przeniesiona wersja zostaje stara (różnica nie znika),
a odtworzone wzorce zostają do przenoszenia kodu — po przeniesieniu --apply jeszcze raz. Kod wyjścia 1 przy porażce.
Każde uruchomienie zapisuje w UPSTREAM.md (sekcja „Stan synchronizacji”) ostatnio sprawdzony commit, datę i wynik oraz
dopisuje wiersz historii. Różnica liczy się od ostatnio sprawdzonego commitu, jeśli był bez zmian, inaczej od przeniesionego.
Zmiana wersji craft-pptx lub craft-drawingml w Cargo.toml DeckCraft wymaga sprawdzenia czytnika przejść (FileTransitions.cs).
"""
import argparse
import datetime
import json
import pathlib
import re
import subprocess
import sys
import tempfile
import tomllib

ROOT = pathlib.Path(__file__).resolve().parent.parent
UPSTREAM = ROOT / "app" / "src" / "Uwielbienia.Core" / "Slideshows" / "Builds" / "UPSTREAM.md"
UPSTREAM_TRANSITIONS = UPSTREAM.with_name("UPSTREAM-TRANSITIONS.md")
FIXTURES = ROOT / "tools" / "deckcraft-anim-fixtures"
CARGO_TOML = FIXTURES / "Cargo.toml"
CARGO_TOMLS = [CARGO_TOML, ROOT / "tools" / "deckcraft-transition-fixtures" / "Cargo.toml"]
TRANSITION_FIXTURES = ROOT / "app" / "tests" / "Uwielbienia.Core.Tests" / "Fixtures" / "DeckCraftTransitions"
TRANSITION_REV = re.compile(r"(rewizja `)([0-9a-f]{7,40})(` \()(\d{4}-\d{2}-\d{2})(\))")
REPO_URL = "https://github.com/storytold/deckcraft"
REV_LINE = re.compile(r"(\*\*Wersja:\*\* `)([0-9a-f]{7,40})(` \()(\d{4}-\d{2}-\d{2})(\))")
CHECKED_LINE = re.compile(r"^- \*\*Ostatnio sprawdzone:\*\* `([0-9a-f]{7,40})` .*$", re.MULTILINE)
CLEAN = "bez zmian w przenoszonych plikach"
PATHS = [
    "crates/anim/src",
    "crates/model/src/anim.rs",
    "crates/pptx/src/convert.rs",
    "crates/ui-egui/src/show.rs",
    "crates/ui-egui/src/show/ui.rs",
    "crates/ui-egui/src/show/tests.rs",
    ":(exclude)crates/anim/src/*test*",
    ":(exclude)crates/anim/src/morph.rs",
    ":(exclude)crates/anim/src/interact.rs",
    ":(exclude)crates/anim/src/show.rs",
]
EFFECT = re.compile(r'\(\s*"([^"]+)",\s*"[^"]*",\s*AnimClass::(\w+)')
TRANSITION = re.compile(r'\(\s*"([^"]+)",\s*"[^"]*",\s*"([^"]+)",\s*\d+')


def git(repo, *args, check=True):
    r = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, encoding="utf-8")
    if check and r.returncode != 0:
        sys.exit(f"git {' '.join(args)}: {r.stderr.strip()}")
    return r.stdout


def prepare_repo(path):
    if path:
        repo = pathlib.Path(path)
        if not (repo / ".git").exists():
            sys.exit(f"{repo} nie jest repozytorium git")
        if git(repo, "remote", check=False).strip():
            git(repo, "fetch", "--quiet", "origin")
        return repo
    repo = pathlib.Path(tempfile.gettempdir()) / "deckcraft-sync"
    if (repo / ".git").exists():
        git(repo, "fetch", "--quiet", "origin")
    else:
        r = subprocess.run(["git", "clone", "--quiet", REPO_URL, str(repo)])
        if r.returncode != 0:
            sys.exit("nie udało się pobrać DeckCraft")
    return repo


def effects(repo, rev):
    src = git(repo, "show", f"{rev}:crates/model/src/anim.rs")
    start = src.find("pub static ANIMATIONS")
    end = src.find("];", start)
    return set(EFFECT.findall(src[start:end])) if start >= 0 else set()


def transitions(repo, rev):
    src = git(repo, "show", f"{rev}:crates/model/src/anim.rs")
    start = src.find("pub static TRANSITIONS")
    end = src.find("];", start)
    return set(TRANSITION.findall(src[start:end])) if start >= 0 else set()


def transition_reader_revisions(repo, rev):
    manifest = tomllib.loads(git(repo, "show", f"{rev}:Cargo.toml"))
    dependencies = manifest.get("workspace", {}).get("dependencies", {})
    reader = {name: dependencies.get(name) for name in ("craft-pptx", "craft-drawingml")}
    return {name: dependency.get("rev") if isinstance(dependency, dict) else None for name, dependency in reader.items()}


def record_check(text, rev, date, result):
    """Linia „Ostatnio sprawdzone” i wiersz historii sprawdzeń (tabela pod nią)."""
    today = datetime.date.today().isoformat()
    line = f"- **Ostatnio sprawdzone:** `{rev}` ({date}), sprawdzono {today} — {result}"
    text = CHECKED_LINE.sub(lambda _: line, text, count=1) if CHECKED_LINE.search(text) else text
    row = f"| {today} | `{rev[:12]}` ({date}) | {result} |"
    header = "|---|---|---|\n"
    at = text.find(header, text.find("**Ostatnio sprawdzone:**"))
    if at >= 0:
        end = at + len(header)
        while end < len(text) and text.startswith("|", end):
            end = text.find("\n", end) + 1 or len(text)
        text = text[:end] + row + "\n" + text[end:]
    return text


def run(cmd, cwd):
    print("$", " ".join(cmd), flush=True)
    try:
        return subprocess.run(cmd, cwd=str(cwd)).returncode
    except FileNotFoundError as error:
        raise FileNotFoundError(f"Nie można uruchomić {cmd[0]}: {error}") from error


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--to", default="origin/main", help="wersja docelowa (domyślnie origin/main)")
    ap.add_argument("--repo", help="własna kopia DeckCraft zamiast katalogu podręcznego")
    ap.add_argument("--apply", action="store_true", help="przestaw wersję, odtwórz fixtures i uruchom testy")
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    text = UPSTREAM.read_text(encoding="utf-8")
    m = REV_LINE.search(text)
    if not m:
        sys.exit(f"brak wersji (**Wersja:** `…` (RRRR-MM-DD)) w {UPSTREAM}")
    ported = m.group(2)
    checked = CHECKED_LINE.search(text)
    # ostatnio sprawdzony commit bez zmian w przenoszonych plikach jest równoważny przeniesionemu — liczymy od niego
    old = checked.group(1) if checked and CLEAN in checked.group(0) else ported
    repo = prepare_repo(args.repo)
    new = git(repo, "rev-parse", f"{args.to}^{{commit}}").strip()
    date = git(repo, "show", "-s", "--format=%cs", new).strip()
    print(f"DeckCraft: przeniesiona {ported[:12]}, różnica od {old[:12]} -> {new[:12]} ({date})\n")

    print(git(repo, "diff", "--stat", old, new, "--", *PATHS) or "(bez zmian w analizowanych plikach Rust)")
    diff = git(repo, "diff", old, new, "--", *PATHS)
    if diff:
        sys.stdout.write(diff + "\n")

    before, after = effects(repo, old), effects(repo, new)
    added, removed = sorted(after - before), sorted(before - after)
    print("Efekty dodane w ANIMATIONS:", ", ".join(f"{i} ({c})" for i, c in added) or "brak")
    print("Efekty usunięte z ANIMATIONS:", ", ".join(f"{i} ({c})" for i, c in removed) or "brak")
    before, after = transitions(repo, old), transitions(repo, new)
    print("Przejścia dodane w TRANSITIONS:", ", ".join(f"{i} ({g})" for i, g in sorted(after - before)) or "brak")
    print("Przejścia usunięte z TRANSITIONS:", ", ".join(f"{i} ({g})" for i, g in sorted(before - after)) or "brak")
    changed = len([l for l in git(repo, "diff", "--name-only", old, new, "--", *PATHS).splitlines() if l.strip()])
    new_kinds = len(after ^ before) + len(effects(repo, old) ^ effects(repo, new))
    reader_before = transition_reader_revisions(repo, old)
    reader_after = transition_reader_revisions(repo, new)
    reader_changed = [name for name in reader_after if reader_before[name] != reader_after[name]]
    for name in reader_changed:
        print(f"Czytnik przejść {name}: {reader_before[name] or 'brak rewizji'} -> {reader_after[name] or 'brak rewizji'}")
    result = CLEAN
    if changed or new_kinds or reader_changed:
        result = f"zmienione pliki Rust: {changed}, efekty / przejścia nowe lub usunięte: {new_kinds}"
        if reader_changed:
            result += f", zmienione rewizje zależności czytnika: {', '.join(reader_changed)}"
        result += " — do sprawdzenia i przeniesienia"

    failure = None
    if args.apply:
        failure = apply(new, date)
        result = f"nieudane --apply: {failure}" if failure else f"przeniesione (--apply), {CLEAN} po przeniesieniu"
        text = UPSTREAM.read_text(encoding="utf-8")
    text = record_check(text, new, date, result)
    UPSTREAM.write_text(text, encoding="utf-8", newline="\n")
    print(f"\nZapisano w UPSTREAM.md: ostatnio sprawdzone {new[:12]} — {result}")
    return 1 if failure else 0


def apply(new, date):
    """Przenosi wersję: nowa wersja we wzorcach i testy. Gdy coś nie przejdzie, przeniesiona wersja zostaje stara
    (różnica pokaże się znowu przy następnym sprawdzeniu); odtworzone wzorce zostają do przenoszenia kodu."""
    previous = {path: path.read_bytes() for path in [UPSTREAM, UPSTREAM_TRANSITIONS, *CARGO_TOMLS]}
    applied = False
    try:
        set_revision(new, date, previous)
        failure = regenerate_and_test(new)
        applied = failure is None
    except (OSError, ValueError) as error:
        failure = str(error)
    finally:
        if not applied:
            restore(previous)

    if failure is not None:
        print("Przeniesiona wersja zostaje bez zmian.")
        return failure
    print("Wzorce odtworzone, testy przechodzą.")
    return None


def set_revision(new, date, previous):
    """Nowa wersja w obu plikach UPSTREAM i w Cargo.toml obu programów wzorców."""
    revision = lambda x: f"{x.group(1)}{new}{x.group(3)}{date}{x.group(5)}"
    UPSTREAM.write_text(REV_LINE.sub(revision, previous[UPSTREAM].decode("utf-8"), count=1), encoding="utf-8", newline="\n")
    UPSTREAM_TRANSITIONS.write_text(TRANSITION_REV.sub(revision, previous[UPSTREAM_TRANSITIONS].decode("utf-8"), count=1), encoding="utf-8", newline="\n")
    for toml in CARGO_TOMLS:
        toml.write_text(re.sub(r'(deckcraft"?, rev = ")[0-9a-f]{7,40}(")', rf"\g<1>{new}\g<2>", previous[toml].decode("utf-8")), encoding="utf-8", newline="\n")


def restore(previous):
    for f, content in previous.items():
        f.write_bytes(content)


def regenerate_and_test(revision):
    """Odtwarza wzorce obu programów i uruchamia testy DeckCraft; zwraca opis porażki albo None."""
    for toml in CARGO_TOMLS:
        if run(["cargo", "run", "--release", "--ignore-rust-version", "--manifest-path", str(toml)], ROOT) != 0:
            return f"Nie udało się odtworzyć wzorców (cargo, {toml.parent.name})."
    for name in ("frames.json", "pptx.json"):
        fixture = json.loads((TRANSITION_FIXTURES / name).read_text(encoding="utf-8"))
        if fixture.get("rev") != revision:
            return f"{name}: oczekiwano rewizji {revision}, zapisano {fixture.get('rev')}."
    if run(["dotnet", "test", "app", "--filter", "DeckCraft"], ROOT) != 0:
        return "Testy nie przechodzą."
    return None


if __name__ == "__main__":
    sys.exit(main())
