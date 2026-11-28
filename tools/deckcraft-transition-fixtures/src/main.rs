//! Writes JSON fixtures from DeckCraft's slide transitions (crates/anim, crates/model, crates/pptx) for the
//! C# port in `app/src/Uwielbienia.Core/Slideshows/Builds` (tests: `DeckCraftTransitionTests`).
//!
//! Usage: `cargo run --release [-- FIXTURE_DIR]` (default: app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftTransitions).
//! - `frames.json`: `transition_frame` for every TRANSITIONS id × option (and the empty option) at several
//!   progress values, `uses_slide_parts`, `layer_mesh` of every layer of a few 3-D frames, and the TRANSITIONS
//!   table with `transition_option_label` of every option.
//! - `pptx.json`: the transitions DeckCraft's own PPTX reader finds in every `pptx/*.pptx` of the fixture
//!   folder (decks made by `make_decks.py`), one entry per slide (`null` = no transition).

use std::path::{Path, PathBuf};

use deckcraft_anim::{Layer, Source, layer_mesh, transition_frame, uses_slide_parts};
use deckcraft_model::anim::{TRANSITIONS, transition_option_label};
use serde_json::{Value, json};

#[path = "../../deckcraft_fixture_revision.rs"]
mod revision;
const TIMES: &[f64] = &[0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0];
const WIDE: f64 = 16.0 / 9.0;
const OLD: f64 = 4.0 / 3.0;

/// Kinds also sampled on a 4:3 slide (effects whose geometry depends on the aspect).
const OLD_ASPECT_KINDS: &[&str] = &[
    "shape", "checker", "blinds", "clock", "ripple", "zoom", "fallOver", "drape", "wind", "prestige", "fracture", "crush", "honeycomb", "glitter",
    "vortex", "shred", "switch", "flip", "gallery", "conveyor", "cube", "box", "rotate", "orbit", "doors", "window", "ferris",
];

/// Numbers rounded to 1e-10: the C# tests compare with a 1e-6 tolerance, and the file stays smaller.
fn num(v: f64) -> Value {
    let r = (v * 1e10).round() / 1e10;
    if r.fract() == 0.0 && r.abs() < 1e15 { json!(r as i64) } else { json!(r) }
}

fn source_name(s: Source) -> &'static str {
    match s {
        Source::Old => "Old",
        Source::New => "New",
        Source::Black => "Black",
        Source::White => "White",
        Source::OldBack => "OldBack",
        Source::NewBack => "NewBack",
        Source::OldContent => "OldContent",
        Source::NewContent => "NewContent",
    }
}

/// A layer as one flat array: source, src l t r b, quad (4 × x y), uv (4 × u v), alpha, fade (4).
fn layer_json(l: &Layer) -> Value {
    let mut v = vec![json!(source_name(l.source))];
    v.extend(l.src.iter().map(|x| num(*x)));
    v.extend(l.quad.iter().flat_map(|p| p.iter().map(|x| num(*x))));
    v.extend(l.uv.iter().flat_map(|p| p.iter().map(|x| num(*x))));
    v.push(num(l.alpha));
    v.extend(l.fade.iter().map(|x| num(*x)));
    Value::Array(v)
}

/// One letter per source (see `sources`).
fn source_letter(s: Source) -> char {
    match s {
        Source::Old => 'O',
        Source::New => 'N',
        Source::Black => 'B',
        Source::White => 'W',
        Source::OldBack => 'o',
        Source::NewBack => 'n',
        Source::OldContent => 'c',
        Source::NewContent => 'd',
    }
}

/// The layers' sources, one letter per layer, runs as letter + count (`BO3N` = Black, Old, Old, Old, New).
fn sources(layers: &[Layer]) -> String {
    let mut out = String::new();
    let mut i = 0;
    while i < layers.len() {
        let s = layers[i].source;
        let n = layers[i..].iter().take_while(|l| l.source == s).count();
        out.push(source_letter(s));
        if n > 1 {
            out += &n.to_string();
        }
        i += n;
    }
    out
}

/// The numbers of a layer in `layer_json` order (without the source).
fn layer_numbers(l: &Layer) -> Vec<f64> {
    let mut v = l.src.to_vec();
    v.extend(l.quad.iter().flat_map(|p| p.iter().copied()));
    v.extend(l.uv.iter().flat_map(|p| p.iter().copied()));
    v.push(l.alpha);
    v.extend(l.fade.iter().copied());
    v
}

/// Checksum of every number of a frame: Σ w(k)·x(k), w(k) = 1 + (k mod 7) / 10 over the numbers of all layers in
/// order (`DeckCraftTransitionTests.Checksum` computes the same).
fn checksum(layers: &[Layer]) -> f64 {
    layers.iter().flat_map(layer_numbers).enumerate().map(|(k, x)| (1.0 + (k % 7) as f64 / 10.0) * x).sum()
}

/// Frames up to this many layers are stored layer by layer; denser ones (particles, tiles, fields) as the layer
/// count, the sources and a checksum of all numbers, except one frame per kind (see `frames`).
const FULL_LAYERS: usize = 48;

fn frames(rev: &str) -> Value {
    let mut cases = Vec::new();
    for (id, _, _, _, opts) in TRANSITIONS {
        let mut options: Vec<&str> = opts.to_vec();
        if !options.contains(&"") {
            options.push("");
        }
        let mut aspects = vec![WIDE];
        if OLD_ASPECT_KINDS.contains(id) {
            aspects.push(OLD);
        }
        for (oi, opt) in options.iter().enumerate() {
            for &aspect in &aspects {
                let all: Vec<Vec<Layer>> = TIMES.iter().map(|&t| transition_frame(id, opt, t, aspect)).collect();
                // Every kind keeps at least one in-between frame layer by layer: the smallest of its first
                // option on a 16:9 slide.
                let detailed = if oi == 0 && aspect == WIDE {
                    (1..TIMES.len() - 1).min_by_key(|&i| all[i].len())
                } else {
                    None
                };
                let frames: Vec<Value> = TIMES
                    .iter()
                    .zip(&all)
                    .enumerate()
                    .map(|(i, (&t, layers))| {
                        let mut f = json!({
                            "t": t,
                            "count": layers.len(),
                            "sources": sources(layers),
                            "checksum": checksum(layers),
                        });
                        if layers.len() <= FULL_LAYERS || detailed == Some(i) {
                            f["layers"] = Value::Array(layers.iter().map(layer_json).collect());
                        }
                        f
                    })
                    .collect();
                cases.push(json!({"kind": id, "option": opt, "aspect": aspect, "usesSlideParts": uses_slide_parts(id, opt), "frames": frames}));
            }
        }
    }
    // Meshes: every layer of a few frames (cards in perspective, wedges, rectangles, soft edges).
    let mut meshes = Vec::new();
    for (kind, opt, t) in
        [("cube", "l", 0.4), ("flip", "r", 0.3), ("gallery", "l", 0.5), ("fallOver", "l", 0.5), ("doors", "vert", 0.3), ("clock", "", 0.5), ("shape", "out", 0.75)]
    {
        for (i, l) in transition_frame(kind, opt, t, WIDE).iter().enumerate() {
            let (v, tris) = layer_mesh(l);
            meshes.push(json!({
                "kind": kind, "option": opt, "t": t, "aspect": WIDE, "layer": i,
                "vertices": v.iter().map(|m| vec![num(m.pos[0]), num(m.pos[1]), num(m.uv[0]), num(m.uv[1]), num(m.a)]).collect::<Vec<_>>(),
                "triangles": tris,
            }));
        }
    }
    // The gallery table itself, with PowerPoint's option labels.
    let catalog: Vec<Value> = TRANSITIONS
        .iter()
        .map(|(id, label, category, ms, opts)| {
            json!({
                "id": id, "label": label, "category": category, "durationMs": ms, "options": opts,
                "optionLabels": opts.iter().map(|o| transition_option_label(id, o)).collect::<Vec<_>>(),
            })
        })
        .collect();
    json!({"rev": rev, "times": TIMES, "catalog": catalog, "cases": cases, "meshes": meshes})
}

fn pptx(dir: &Path, rev: &str) -> Value {
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir.join("pptx"))
        .map(|r| r.filter_map(|e| e.ok().map(|e| e.path())).filter(|p| p.extension().is_some_and(|x| x == "pptx")).collect())
        .unwrap_or_default();
    files.sort();
    let mut out = serde_json::Map::new();
    for f in files {
        let name = f.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        let bytes = std::fs::read(&f).unwrap_or_else(|e| panic!("{name}: {e}"));
        let p = deckcraft_pptx::import(&bytes).unwrap_or_else(|e| panic!("{name}: {e}"));
        let slides: Vec<Value> = p
            .slides
            .iter()
            .map(|s| match &s.transition {
                Some(t) => json!({"kind": t.kind, "option": t.option, "durationMs": t.duration_ms}),
                None => Value::Null,
            })
            .collect();
        out.insert(name, Value::Array(slides));
    }
    json!({"rev": rev, "files": out})
}

fn main() {
    let dir = std::env::args().nth(1).map(PathBuf::from).unwrap_or_else(|| {
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftTransitions")
    });
    std::fs::create_dir_all(&dir).expect("fixture folder");
    let write = |name: &str, v: &Value| {
        let text = serde_json::to_string(v).expect("json") + "\n";
        std::fs::write(dir.join(name), &text).expect("write");
        println!("{name}: {} bytes", text.len());
    };
    let rev = revision::upstream_rev(include_str!("../Cargo.toml"));
    write("frames.json", &frames(rev));
    write("pptx.json", &pptx(&dir, rev));
}
