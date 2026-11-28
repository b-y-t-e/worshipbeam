"""Builds the small .pptx decks whose transitions DeckCraft's PPTX reader reads back into pptx.json.

Usage: python -I tools/deckcraft-transition-fixtures/make_decks.py [DECKCRAFT_CLI]
(default CLI: C:/Program Files/DeckCraft/deckcraft-cli.exe). Writes
app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftTransitions/pptx/*.pptx; then run the Cargo tool
(cargo run --release) to write pptx.json from them.

- kinds-N.pptx: every transition kind x option, set by DeckCraft itself (transition.set), with default
  and explicit durations — DeckCraft writes p14/p15 effects wrapped in mc:AlternateContent.
- handwritten.pptx: hand-written <p:transition> XML (AlternateContent with p14 choice and fallback, an
  unsupported Requires prefix, spd slow/med/fast, p15:prstTrans with invX, an unknown effect, advTm
  only, a hidden slide, a slide without transition).
"""

import json
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftTransitions/pptx"
CLI = sys.argv[1] if len(sys.argv) > 1 else "C:/Program Files/DeckCraft/deckcraft-cli.exe"
PER_DECK = 45

NS_P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main"
NS_P15 = "http://schemas.microsoft.com/office/powerpoint/2012/main"
NS_MC = "http://schemas.openxmlformats.org/markup-compatibility/2006"

# Hand-written transitions, one per slide (None = slide without transition).
HANDWRITTEN = [
    # PowerPoint 2010+: p14 effect with a p:fade fallback, duration in p14:dur.
    f'<mc:AlternateContent xmlns:mc="{NS_MC}"><mc:Choice xmlns:p14="{NS_P14}" Requires="p14">'
    '<p:transition spd="slow" p14:dur="2250"><p14:vortex dir="r"/></p:transition></mc:Choice>'
    '<mc:Fallback><p:transition spd="slow"><p:fade/></p:transition></mc:Fallback></mc:AlternateContent>',
    # A choice the reader does not understand: the fallback wins.
    f'<mc:AlternateContent xmlns:mc="{NS_MC}"><mc:Choice xmlns:p99="urn:example:p99" Requires="p99">'
    '<p:transition p99:dur="3000"><p99:sparkle/></p:transition></mc:Choice>'
    '<mc:Fallback><p:transition spd="med"><p:push dir="u"/></p:transition></mc:Fallback></mc:AlternateContent>',
    '<p:transition spd="slow"><p:dissolve/></p:transition>',
    '<p:transition spd="slow"><p:wipe dir="d"/></p:transition>',
    '<p:transition spd="med"><p:fade thruBlk="1"/></p:transition>',
    '<p:transition spd="fast"><p:cut thruBlk="true"/></p:transition>',
    '<p:transition><p:randomBar dir="vert"/></p:transition>',
    '<p:transition><p:strips dir="rd"/></p:transition>',
    '<p:transition><p:split orient="vert" dir="in"/></p:transition>',
    '<p:transition><p:zoom dir="in"/></p:transition>',
    '<p:transition><p:wheelReverse spokes="1"/></p:transition>',
    f'<mc:AlternateContent xmlns:mc="{NS_MC}"><mc:Choice xmlns:p15="{NS_P15}" Requires="p15">'
    '<p:transition spd="slow" p14:dur="2000" xmlns:p14="' + NS_P14 + '"><p15:prstTrans prst="wind" invX="1"/></p:transition>'
    '</mc:Choice><mc:Fallback><p:transition spd="slow"><p:fade/></p:transition></mc:Fallback></mc:AlternateContent>',
    f'<p:transition xmlns:p15="{NS_P15}"><p15:prstTrans prst="fallOver" invX="1"/></p:transition>',
    f'<p:transition xmlns:p15="{NS_P15}"><p15:prstTrans prst="pageCurlSingle" invX="1"/></p:transition>',
    f'<p:transition xmlns:p15="{NS_P15}"><p15:prstTrans prst="somethingNew"/></p:transition>',
    f'<p:transition xmlns:p14="{NS_P14}" p14:dur="1600"><p14:prism isContent="1" isInverted="1" dir="u"/></p:transition>',
    f'<p:transition xmlns:p14="{NS_P14}"><p14:glitter pattern="hexagon" dir="d"/></p:transition>',
    f'<p:transition xmlns:p14="{NS_P14}"><p14:shred pattern="crush" dir="out"/></p:transition>',
    f'<p:transition xmlns:p14="{NS_P14}"><p14:flythrough dir="out" hasBounce="1"/></p:transition>',
    f'<p:transition xmlns:p14="{NS_P14}"><p14:honeycomb/></p:transition>',
    '<p:transition><p:newsflash/></p:transition>',
    '<p:transition><p:unknownEffect/></p:transition>',
    '<p:transition advClick="0" advTm="3000"/>',
    '<p:transition/>',
    None,
    '<p:transition dur="1234"><p:cover dir="ru"/></p:transition>',
]
HIDDEN = {3}  # slide indices marked hidden (show="0")


def cli(*args: str) -> str:
    return subprocess.run([CLI, *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


def build(path: Path, commands: list[str]) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        cmds = Path(tmp) / "commands.txt"
        cmds.write_text("\n".join(commands) + "\n", encoding="utf-8")
        cli("run", "--cmd-file", str(cmds), "--save", str(path))


def blank_slides(n: int) -> list[str]:
    return ['slide.new={"layout":"blank"}'] * (n - 1)


def kinds_decks() -> None:
    listing = cli("run", "--cmd", "transition.list", "--print")
    kinds = json.loads(listing[listing.index("["):])
    cases = []
    for k in kinds:
        options = k["options"] or [""]
        for i, opt in enumerate(options):
            # Every other case gets an explicit duration, the rest DeckCraft's default.
            duration = None if (len(cases) % 2 == 0) else 300 + 150 * (len(cases) % 13)
            cases.append((k["id"], opt, duration))
    for d in range(0, len(cases), PER_DECK):
        chunk = cases[d:d + PER_DECK]
        cmds = blank_slides(len(chunk))
        for i, (kind, opt, duration) in enumerate(chunk):
            p = {"kind": kind, "index": i}
            if opt:
                p["option"] = opt
            if duration is not None:
                p["duration"] = duration
            cmds.append("transition.set=" + json.dumps(p))
        build(OUT / f"kinds-{d // PER_DECK + 1}.pptx", cmds)


def handwritten_deck() -> None:
    path = OUT / "handwritten.pptx"
    build(path, blank_slides(len(HANDWRITTEN)))
    with zipfile.ZipFile(path) as z:
        entries = [(i, z.read(i.filename)) for i in z.infolist()]
    pres = next(data for i, data in entries if i.filename == "ppt/presentation.xml").decode("utf-8")
    rels = next(data for i, data in entries if i.filename == "ppt/_rels/presentation.xml.rels").decode("utf-8")
    targets = dict(re.findall(r'Id="([^"]+)"[^>]*Target="([^"]+)"', rels))
    targets.update({a: b for b, a in re.findall(r'Target="([^"]+)"[^>]*Id="([^"]+)"', rels)})
    order = ["ppt/" + targets[rid] for rid in re.findall(r'<p:sldId [^>]*r:id="([^"]+)"', pres)]
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for info, data in entries:
            if info.filename in order:
                index = order.index(info.filename)
                xml = data.decode("utf-8")
                xml = re.sub(r"<mc:AlternateContent[^>]*>(?:(?!</mc:AlternateContent>).)*<p:transition.*?</mc:AlternateContent>", "", xml, flags=re.S)
                xml = re.sub(r"<p:transition\b.*?(?:/>|</p:transition>)", "", xml, flags=re.S)
                if HANDWRITTEN[index] is not None:
                    at = xml.find("<p:timing") if "<p:timing" in xml else xml.rfind("</p:sld>")
                    xml = xml[:at] + HANDWRITTEN[index] + xml[at:]
                if index in HIDDEN:
                    xml = xml.replace("<p:sld ", '<p:sld show="0" ', 1)
                data = xml.encode("utf-8")
            z.writestr(info, data)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.pptx"):
        old.unlink()
    kinds_decks()
    handwritten_deck()
    for f in sorted(OUT.glob("*.pptx")):
        print(f.name, f.stat().st_size)


if __name__ == "__main__":
    main()
