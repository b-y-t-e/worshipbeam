//! Writes JSON fixtures from DeckCraft's animation timing (crates/anim, crates/model) for the C# port in
//! `app/src/Uwielbienia.Core/Slideshows/Builds` (tests: `DeckCraftAnimTests`).
//!
//! Usage: `cargo run --release [-- OUTPUT_DIR]` (default: app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftAnim).
//! Uses the public API only: `Timeline::new`, `Timeline::state`/`state_with`, the `ANIMATIONS` table.

use std::fmt::Write as _;
use std::path::PathBuf;

use deckcraft_anim::{AnimState, Timeline, TriggerPlay};
use deckcraft_model::anim::{ANIMATIONS, TextBuild};
use deckcraft_model::{AnimClass, AnimStart, Animation, ColorRef, Fill, Paragraph, Rgba, SchemeSlot, Shape, ShapeId, Slide, TextBody, Xfrm};
use serde_json::{Value, json};

const SLIDE_W: f64 = 960.0;
const SLIDE_H: f64 = 540.0;

fn class_name(c: AnimClass) -> &'static str {
    match c {
        AnimClass::Entrance => "entrance",
        AnimClass::Emphasis => "emphasis",
        AnimClass::Exit => "exit",
        AnimClass::Path => "path",
        AnimClass::Media => "media",
    }
}

fn hex(c: Rgba) -> String {
    format!("#{:02X}{:02X}{:02X}{:02X}", c.r, c.g, c.b, c.a)
}

fn state_json(s: &AnimState) -> Value {
    json!({
        "visible": s.visible,
        "opacity": s.opacity,
        "offsetX": s.offset_x,
        "offsetY": s.offset_y,
        "scaleX": s.scale_x,
        "scaleY": s.scale_y,
        "rotate": s.rotate,
        "clip": s.clip.map(|c| json!(c)),
        "tint": s.tint.map(hex),
    })
}

/// A shape with a box, an optional solid fill and optional paragraphs `(level, text)`.
fn shape(id: u32, xfrm: Option<Xfrm>, fill: Option<Fill>, paras: Option<&[(u8, &str)]>) -> Shape {
    let text = paras.map(|ps| TextBody {
        paragraphs: ps
            .iter()
            .map(|(lvl, t)| {
                let mut p = Paragraph::new(t);
                p.level = *lvl;
                p
            })
            .collect(),
        ..Default::default()
    });
    Shape { id: ShapeId(id), xfrm, fill, text, ..Default::default() }
}

fn solid(r: u8, g: u8, b: u8) -> Option<Fill> {
    Some(Fill::Solid { color: ColorRef::rgb(Rgba::rgb(r, g, b)) })
}

fn bx(x: f64, y: f64, w: f64, h: f64) -> Option<Xfrm> {
    Some(Xfrm::new(x, y, w, h))
}

/// The shape as the C# `TimelineShape` sees it: box, solid fill colour and paragraph infos.
fn shape_json(s: &Shape) -> Value {
    let fill = match &s.fill {
        Some(Fill::Solid { color }) => match &color.base {
            deckcraft_model::ColorBase::Rgb { rgb } => Some(hex(*rgb)),
            _ => None,
        },
        _ => None,
    };
    json!({
        "id": s.id.0,
        "box": s.xfrm.map(|x| json!([x.x, x.y, x.w, x.h])),
        "fill": fill,
        "paragraphs": s.text.as_ref().map(|t| t.paragraphs.iter().map(|p| json!({
            "level": p.level,
            "filled": deckcraft_model::anim::build_filled(p),
        })).collect::<Vec<_>>()),
    })
}

fn anim(shape: u32, class: AnimClass, effect: &str) -> Animation {
    Animation { shape: ShapeId(shape), class, effect: effect.into(), duration_ms: 1000, ..Default::default() }
}

fn with(a: Animation, f: impl FnOnce(&mut Animation)) -> Animation {
    let mut a = a;
    f(&mut a);
    a
}

fn effects_fixture(rev: &str) -> String {
    let sh = shape(2, bx(100.0, 150.0, 200.0, 120.0), solid(0x33, 0x99, 0xCC), None);
    let mut ids = Vec::new();
    let mut lines = Vec::new();
    let ts: Vec<f64> = (0..=20).map(|k| k as f64 * 0.05).chain([1.2]).collect();
    for (id, _label, class, _preset, _dur, options) in ANIMATIONS {
        ids.push(json!({"id": id, "class": class_name(*class), "options": options}));
        let mut opts: Vec<&str> = vec![""];
        opts.extend(options.iter().copied());
        for opt in opts {
            let a = with(anim(2, *class, id), |a| a.option = opt.into());
            let slide = Slide { shapes: vec![sh.clone()], animations: vec![a], ..Default::default() };
            let tl = Timeline::new(&slide, SLIDE_W, SLIDE_H);
            let mut samples: Vec<Value> = ts.iter().map(|&t| json!({"step": 0, "t": t, "state": state_json(&tl.state(ShapeId(2), None, 0, t))})).collect();
            samples.push(json!({"step": 1, "t": 0.0, "state": state_json(&tl.state(ShapeId(2), None, 1, 0.0))}));
            lines.push(json!({"effect": id, "class": class_name(*class), "option": opt, "samples": samples}).to_string());
        }
    }
    let mut out = String::new();
    let _ = writeln!(out, "{{");
    let _ = writeln!(out, "  \"rev\": {},", json!(rev));
    let _ = writeln!(out, "  \"slide\": [{SLIDE_W:?}, {SLIDE_H:?}],");
    let _ = writeln!(out, "  \"shape\": {},", shape_json(&sh));
    let _ = writeln!(out, "  \"durationMs\": 1000,");
    let _ = writeln!(out, "  \"effectIds\": [");
    let n = ids.len();
    for (i, v) in ids.iter().enumerate() {
        let _ = writeln!(out, "    {}{}", v, if i + 1 < n { "," } else { "" });
    }
    let _ = writeln!(out, "  ],");
    let _ = writeln!(out, "  \"cases\": [");
    let n = lines.len();
    for (i, l) in lines.iter().enumerate() {
        let _ = writeln!(out, "    {}{}", l, if i + 1 < n { "," } else { "" });
    }
    let _ = writeln!(out, "  ]");
    let _ = writeln!(out, "}}");
    out
}

struct Scenario {
    name: &'static str,
    shapes: Vec<Shape>,
    animations: Vec<Animation>,
    /// Trigger playheads to sample with `state_with`: (trigger shape, step, t).
    trigger_plays: Vec<(u32, usize, f64)>,
}

fn scenarios() -> Vec<Scenario> {
    use AnimClass::*;
    use AnimStart::*;
    let a = anim;
    let s2 = || shape(2, bx(100.0, 150.0, 200.0, 120.0), solid(0x33, 0x99, 0xCC), None);
    let s3 = || shape(3, bx(500.0, 80.0, 150.0, 300.0), solid(0xEE, 0x88, 0x11), None);
    let s4 = || shape(4, bx(-50.0, 400.0, 300.0, 100.0), None, None);
    let s5 = || shape(5, bx(800.0, 400.0, 100.0, 100.0), solid(0x10, 0x10, 0x10), None);
    vec![
        Scenario {
            name: "mixedStarts",
            shapes: vec![s2(), s3(), s4()],
            animations: vec![
                a(2, Entrance, "fade"),
                with(a(3, Entrance, "fly"), |x| {
                    x.start = WithPrevious;
                    x.delay_ms = 200;
                    x.option = "l".into();
                }),
                with(a(4, Entrance, "zoom"), |x| {
                    x.start = AfterPrevious;
                    x.delay_ms = 300;
                    x.duration_ms = 500;
                }),
                with(a(2, Emphasis, "pulse"), |x| x.duration_ms = 750),
                with(a(3, Exit, "wipeOut"), |x| {
                    x.start = AfterPrevious;
                    x.option = "r".into();
                }),
                with(a(4, Exit, "flyOut"), |x| {
                    x.start = WithPrevious;
                    x.delay_ms = 500;
                    x.option = "rb".into();
                }),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "autoFirstStep",
            shapes: vec![s2(), s3()],
            animations: vec![
                with(a(2, Entrance, "fly"), |x| x.start = WithPrevious),
                with(a(3, Entrance, "split"), |x| {
                    x.start = AfterPrevious;
                    x.option = "vertOut".into();
                }),
                with(a(2, Emphasis, "teeter"), |x| x.start = OnClick),
                with(a(3, Entrance, "appear"), |x| {
                    x.start = AfterPrevious;
                    x.duration_ms = 0;
                }),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "afterPreviousFirst",
            shapes: vec![s2()],
            animations: vec![with(a(2, Entrance, "bounce"), |x| {
                x.start = AfterPrevious;
                x.delay_ms = 400;
            })],
            trigger_plays: vec![],
        },
        Scenario {
            name: "repeats",
            shapes: vec![s2(), s3()],
            animations: vec![
                with(a(2, Emphasis, "spin"), |x| {
                    x.repeat = 3;
                    x.duration_ms = 500;
                }),
                with(a(3, Emphasis, "pulse"), |x| {
                    x.repeat = u32::MAX;
                    x.start = WithPrevious;
                }),
                with(a(3, Emphasis, "wave"), |x| x.duration_ms = 400),
                with(a(2, Path, "lines"), |x| {
                    x.start = WithPrevious;
                    x.repeat = u32::MAX;
                    x.auto_reverse = true;
                }),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "autoReverseRewind",
            shapes: vec![s2(), s3(), s4()],
            animations: vec![
                with(a(2, Emphasis, "growShrink"), |x| x.auto_reverse = true),
                with(a(3, Entrance, "fly"), |x| {
                    x.start = WithPrevious;
                    x.auto_reverse = true;
                    x.option = "t".into();
                }),
                with(a(4, Entrance, "fade"), |x| {
                    x.start = WithPrevious;
                    x.rewind = true;
                }),
                with(a(2, Exit, "fadeOut"), |x| x.rewind = true),
                with(a(3, Path, "arcs"), |x| {
                    x.start = WithPrevious;
                    x.rewind = true;
                }),
                with(a(4, Emphasis, "spin"), |x| {
                    x.start = WithPrevious;
                    x.auto_reverse = true;
                    x.repeat = 2;
                }),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "afterActions",
            shapes: vec![s2(), s3(), s4(), s5()],
            animations: vec![
                with(a(2, Entrance, "fade"), |x| x.after = Some("hide".into())),
                with(a(3, Entrance, "fade"), |x| {
                    x.start = WithPrevious;
                    x.after = Some("hideOnNextClick".into());
                }),
                with(a(4, Entrance, "fade"), |x| {
                    x.start = WithPrevious;
                    x.after = Some("#808080".into());
                }),
                with(a(5, Emphasis, "darken"), |x| {
                    x.start = WithPrevious;
                    x.after = Some("#20406080".into());
                }),
                with(a(5, Entrance, "fade"), |x| {
                    x.start = WithPrevious;
                    x.after = Some("bogus".into());
                }),
                a(2, Emphasis, "pulse"),
                a(3, Emphasis, "lighten"),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "exitThenEntrance",
            shapes: vec![s2(), s3()],
            animations: vec![
                a(2, Exit, "fadeOut"),
                with(a(2, Entrance, "fly"), |x| x.option = "b".into()),
                with(a(3, Entrance, "grow"), |x| x.start = WithPrevious),
                with(a(3, Exit, "shrinkTurn"), |x| x.start = AfterPrevious),
                with(a(3, Entrance, "sinkDown"), |x| x.class = Exit),
                with(a(3, Entrance, "float"), |x| x.option = "d".into()),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "byParagraph",
            shapes: vec![
                shape(6, bx(60.0, 100.0, 400.0, 300.0), solid(0xFF, 0xFF, 0xFF), Some(&[(1, "Before"), (0, "Lead"), (0, " "), (1, "Sub"), (0, "Next"), (0, ""), (2, "Deep")])),
                shape(7, bx(500.0, 100.0, 400.0, 300.0), None, Some(&[(0, "One"), (0, "Two"), (0, "Three")])),
                shape(8, bx(500.0, 420.0, 400.0, 100.0), None, Some(&[(0, ""), (1, "  ")])),
                shape(9, bx(60.0, 420.0, 400.0, 100.0), None, Some(&[(0, "A"), (0, "B"), (0, "C")])),
            ],
            animations: vec![
                with(a(6, Entrance, "fly"), |x| {
                    x.text_build = TextBuild::ByParagraph;
                    x.option = "l".into();
                }),
                with(a(7, Entrance, "fade"), |x| {
                    x.text_build = TextBuild::ByParagraph;
                    x.start = WithPrevious;
                }),
                with(a(7, Emphasis, "pulse"), |x| {
                    x.text_build = TextBuild::ByParagraph;
                    x.start = AfterPrevious;
                    x.delay_ms = 250;
                }),
                with(a(8, Entrance, "wipe"), |x| x.text_build = TextBuild::ByParagraph),
                with(a(9, Entrance, "appear"), |x| x.paragraph = Some(1)),
                with(a(9, Entrance, "fade"), |x| {
                    x.paragraph = Some(2);
                    x.start = AfterPrevious;
                }),
                with(a(6, Exit, "fadeOut"), |x| x.text_build = TextBuild::ByParagraph),
                with(a(9, Entrance, "zoom"), |x| x.text_build = TextBuild::ByWord),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "triggers",
            shapes: vec![s2(), s3(), s4(), s5()],
            animations: vec![
                a(2, Entrance, "fade"),
                with(a(3, Entrance, "fly"), |x| {
                    x.trigger = Some(ShapeId(5));
                    x.start = WithPrevious;
                }),
                with(a(3, Emphasis, "spin"), |x| {
                    x.trigger = Some(ShapeId(5));
                    x.start = WithPrevious;
                    x.delay_ms = 500;
                }),
                with(a(4, Entrance, "wipe"), |x| x.trigger = Some(ShapeId(5))),
                with(a(2, Emphasis, "pulse"), |x| x.trigger = Some(ShapeId(4))),
                with(a(4, Exit, "fadeOut"), |x| x.start = OnClick),
            ],
            trigger_plays: vec![(5, 0, 0.5), (5, 0, 1.2), (5, 1, 0.3), (5, 2, 0.0), (4, 0, 0.5), (4, 1, 0.0)],
        },
        Scenario {
            name: "smoothing",
            shapes: vec![s2(), s3(), s4()],
            animations: vec![
                with(a(2, Entrance, "fly"), |x| {
                    x.smooth_start = 0.3;
                    x.smooth_end = 0.3;
                }),
                with(a(3, Entrance, "fade"), |x| {
                    x.start = WithPrevious;
                    x.bounce_end = 0.4;
                }),
                with(a(4, Entrance, "zoom"), |x| {
                    x.start = WithPrevious;
                    x.smooth_start = 0.8;
                    x.smooth_end = 0.6;
                }),
                with(a(2, Path, "customPath"), |x| {
                    x.path = Some("M 0 0 L 0.2 0.1 E".into());
                    x.smooth_end = 1.0;
                }),
                with(a(3, Emphasis, "spin"), |x| {
                    x.start = WithPrevious;
                    x.bounce_end = 1.0;
                }),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "motionPaths",
            shapes: vec![s2()],
            animations: vec![
                with(a(2, Path, "customPath"), |x| {
                    x.path = Some("M 0 0 C 0.1 0 0.2 0.1 0.2 0.2 L 0.3 0.3 Q 0.4 0.2 0.5 0.3 H 0.6 V 0.1 Z E".into())
                }),
                with(a(2, Path, "customPath"), |x| x.path = Some("m 0.01 0.02 l 0.1 0.1 h 0.1 v -0.1 c 0 0.1 0.1 0.1 0.1 0 q 0.05 -0.1 0.1 0 z".into())),
                with(a(2, Path, "customPath"), |x| x.path = Some("M 0 0 L 1e-1 2E-2 L .5 -.25 L +0.1,0.3 garbage 0.9 0.9".into())),
                with(a(2, Path, "lines"), |x| x.option = "up".into()),
                with(a(2, Path, "arcs"), |x| x.option = "left".into()),
                with(a(2, Path, "turns"), |x| x.option = "right".into()),
                with(a(2, Path, "shapes"), |x| x.option = "square".into()),
                a(2, Path, "loops"),
                a(2, Path, "heart"),
                a(2, Path, "spiralLeft"),
                a(2, Path, "star5"),
                with(a(2, Path, "lines"), |x| x.path = Some("   ".into())),
                with(a(2, Path, "customPath"), |x| x.path = Some("M 0 0 L 0.1".into())),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "amounts",
            shapes: vec![s2(), s3(), s4()],
            animations: vec![
                with(a(2, Emphasis, "spin"), |x| {
                    x.amount = Some(90.0);
                    x.option = "counterClockwise".into();
                }),
                with(a(3, Emphasis, "growShrink"), |x| {
                    x.start = WithPrevious;
                    x.amount = Some(2.0);
                    x.option = "horz".into();
                }),
                with(a(4, Emphasis, "transparency"), |x| {
                    x.start = WithPrevious;
                    x.amount = Some(0.25);
                }),
                with(a(3, Emphasis, "growShrink"), |x| x.option = "vert".into()),
                with(a(2, Emphasis, "spin"), |x| {
                    x.start = WithPrevious;
                    x.amount = Some(-720.0);
                }),
                with(a(4, Emphasis, "growShrink"), |x| {
                    x.start = WithPrevious;
                    x.amount = Some(-1.0);
                }),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "colors",
            shapes: vec![s2(), s3(), s4(), shape(10, bx(300.0, 300.0, 50.0, 50.0), Some(Fill::None), None)],
            animations: vec![
                with(a(2, Emphasis, "objectColor"), |x| x.color = Some(ColorRef::rgb(Rgba::rgb(0x00, 0xAA, 0x00)))),
                with(a(3, Emphasis, "colorPulse"), |x| {
                    x.start = WithPrevious;
                    x.color = Some(ColorRef::rgb(Rgba::rgba(0x10, 0x20, 0x30, 0x80)));
                }),
                with(a(4, Emphasis, "desaturate"), |x| x.start = WithPrevious),
                with(a(10, Emphasis, "complementaryColor"), |x| x.start = WithPrevious),
                a(2, Emphasis, "darken"),
                with(a(3, Emphasis, "fillColor"), |x| {
                    x.start = WithPrevious;
                    x.color = Some(ColorRef::scheme(SchemeSlot::Accent2));
                }),
                with(a(4, Emphasis, "fontColor"), |x| x.start = WithPrevious),
                with(a(10, Emphasis, "boldFlash"), |x| x.start = WithPrevious),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "media",
            shapes: vec![s2(), s3()],
            animations: vec![
                with(a(2, Media, "play"), |x| x.delay_ms = 300),
                with(a(3, Entrance, "fade"), |x| x.start = AfterPrevious),
                with(a(2, Media, "stop"), |x| x.start = OnClick),
            ],
            trigger_plays: vec![],
        },
        Scenario {
            name: "missingShapes",
            shapes: vec![shape(11, None, solid(1, 2, 3), None)],
            animations: vec![
                with(a(11, Entrance, "fly"), |x| x.option = "r".into()),
                with(a(12, Entrance, "peek"), |x| {
                    x.start = WithPrevious;
                    x.option = "t".into();
                }),
                with(a(12, Emphasis, "colorPulse"), |x| x.start = AfterPrevious),
            ],
            trigger_plays: vec![],
        },
    ]
}

fn timelines_fixture(rev: &str) -> String {
    let ts = [0.0, 0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 5.0];
    let mut lines = Vec::new();
    for sc in scenarios() {
        let slide = Slide { shapes: sc.shapes.clone(), animations: sc.animations.clone(), ..Default::default() };
        let tl = Timeline::new(&slide, SLIDE_W, SLIDE_H);
        let steps = tl.steps();
        // Targets: every shape on the slide and every animated id, whole shape and each paragraph.
        let mut ids: Vec<u32> = sc.shapes.iter().map(|s| s.id.0).collect();
        for a in &sc.animations {
            if !ids.contains(&a.shape.0) {
                ids.push(a.shape.0);
            }
        }
        let mut targets: Vec<(u32, Option<usize>)> = Vec::new();
        for id in &ids {
            targets.push((*id, None));
            let n = sc.shapes.iter().find(|s| s.id.0 == *id).and_then(|s| s.text.as_ref()).map_or(0, |t| t.paragraphs.len());
            for p in 0..n {
                targets.push((*id, Some(p)));
            }
        }
        let mut states = Vec::new();
        for (id, para) in &targets {
            for step in 0..=steps {
                for &t in &ts {
                    states.push(json!({"shape": id, "paragraph": para, "step": step, "t": t, "state": state_json(&tl.state(ShapeId(*id), *para, step, t))}));
                }
            }
        }
        let mut trigger_states = Vec::new();
        for &(trigger, step, t) in &sc.trigger_plays {
            let play = [TriggerPlay { trigger: ShapeId(trigger), step, t }];
            for (id, para) in &targets {
                for main_step in [0, steps] {
                    trigger_states.push(json!({
                        "trigger": trigger, "triggerStep": step, "triggerT": t,
                        "shape": id, "paragraph": para, "step": main_step, "t": 0.0,
                        "state": state_json(&tl.state_with(ShapeId(*id), *para, main_step, 0.0, &play)),
                    }));
                }
            }
        }
        let effects: Vec<Value> = tl
            .effects()
            .iter()
            .map(|e| {
                json!({
                    "anim": e.anim, "shape": e.shape.0, "paragraph": e.paragraph,
                    "trigger": match e.sequence { deckcraft_anim::Sequence::Main => None, deckcraft_anim::Sequence::Trigger(s) => Some(s.0) },
                    "step": e.step, "start": e.start, "duration": e.duration, "iterations": e.iterations,
                    "untilNextClick": e.until_next_click, "autoReverse": e.auto_reverse, "rewind": e.rewind,
                    "span": e.span, "class": class_name(e.class),
                })
            })
            .collect();
        let locate: Vec<Value> = [0.0, 0.3, 1.0, 1.7, 2.5, 4.0, 10.0, -1.0]
            .iter()
            .map(|&t| {
                let (s, lt) = tl.locate(t);
                json!({"time": t, "step": s, "t": lt})
            })
            .collect();
        let v = json!({
            "name": sc.name,
            "slide": [SLIDE_W, SLIDE_H],
            "shapes": sc.shapes.iter().map(shape_json).collect::<Vec<_>>(),
            "animations": serde_json::to_value(&sc.animations).unwrap_or_default(),
            "steps": steps,
            "stepIsAuto": (0..=steps).map(|i| tl.step_is_auto(i)).collect::<Vec<_>>(),
            "stepDuration": (0..=steps).map(|i| tl.step_duration(i)).collect::<Vec<_>>(),
            "totalDuration": tl.total_duration(),
            "isEmpty": tl.is_empty(),
            "animatedShapes": tl.animated_shapes().iter().map(|s| s.0).collect::<Vec<_>>(),
            "paragraphTargets": ids.iter().map(|id| json!({"shape": id, "paragraphs": tl.paragraph_targets(ShapeId(*id))})).collect::<Vec<_>>(),
            "triggerShapes": tl.trigger_shapes().iter().map(|s| s.0).collect::<Vec<_>>(),
            "locate": locate,
            "effects": effects,
            "states": states,
            "triggerStates": trigger_states,
        });
        lines.push(v.to_string());
    }
    let mut out = String::new();
    let _ = writeln!(out, "{{");
    let _ = writeln!(out, "  \"rev\": {},", json!(rev));
    let _ = writeln!(out, "  \"scenarios\": [");
    let n = lines.len();
    for (i, l) in lines.iter().enumerate() {
        let _ = writeln!(out, "    {}{}", l, if i + 1 < n { "," } else { "" });
    }
    let _ = writeln!(out, "  ]");
    let _ = writeln!(out, "}}");
    out
}

#[path = "../../deckcraft_fixture_revision.rs"]
mod revision;

fn main() -> std::io::Result<()> {
    let out: PathBuf = std::env::args()
        .nth(1)
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../app/tests/Uwielbienia.Core.Tests/Fixtures/DeckCraftAnim"));
    std::fs::create_dir_all(&out)?;
    let rev = revision::upstream_rev(include_str!("../Cargo.toml"));
    std::fs::write(out.join("effects.json"), effects_fixture(rev))?;
    std::fs::write(out.join("timelines.json"), timelines_fixture(rev))?;
    println!("fixtures written to {}", out.display());
    Ok(())
}
