import { useEffect, useState } from "react";
import { command, type Snapshot } from "../connection";
import { preloadSlideImage, SLIDE_WIDTH, THUMBNAIL_WIDTH, useSlideImage } from "../images";
import { Lines } from "./Lines";
import { PauseIcon, PlayIcon } from "./Icons";
import { getLanguage, t } from "../i18n";

const formatTime = (seconds: number) => {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = String(s % 60).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${r}` : `${m}:${r}`;
};

/**
 * Kolumna „Na ekranie”: to, co widzi sala. Slajdy pozycji (klik = ten slajd), bieżący z akordami, następny,
 * Wstecz / Dalej, czarny ekran; przy filmie — czas, pauza, ±15 s; przy kiosku — bieżący element; slajd prezentacji — obraz.
 */
export function LiveColumn({ snapshot, chords }: { snapshot: Snapshot; chords: boolean }) {
  const { state, video, kiosk } = snapshot;
  const hasItem = !!state?.itemId || !!state?.songId;
  const kioskShown = !!state?.isKiosk && !!kiosk && kiosk.itemId === state.itemId;
  const image = useSlideImage(state?.image, SLIDE_WIDTH);
  // następny slajd: miniatura od razu, a pełny podgląd pobrany zawczasu — po „Dalej” bez czekania
  const nextImage = useSlideImage(state?.nextImage, THUMBNAIL_WIDTH);
  useEffect(() => preloadSlideImage(state?.nextImage, SLIDE_WIDTH), [state?.nextImage]);

  return (
    <section className="column live-column">
      <header className="column-head">
        <h2>{t("Na ekranie")}</h2>
        {hasItem ? (
          <button className={`toggle${state?.isBlank ? " on-candle" : ""}`} aria-pressed={!!state?.isBlank}
                  title={t("Sala nie widzi tekstu; tekst zostaje (klawisz kropka)")} onClick={() => void command("blank")}>
            {t("Czarny ekran")}
          </button>
        ) : null}
      </header>
      {!hasItem ? <p className="muted empty">{t("Ekran jest pusty. Dwuklik w planie pokazuje pozycję.")}</p> : (
        <div className="scroll">
          <div className="live-head">
            {state!.number ? <span className="live-number">{state!.number}</span> : null}
            <span className="live-name">{state!.title}</span>
            {state!.slideCount > 1 ? <span className="live-count">{t("{0} z {1}", state!.slideIndex + 1, state!.slideCount)}</span> : null}
          </div>
          <div className="live-progress" aria-hidden="true">
            <div style={{ width: `${(100 * (state!.slideIndex + 1)) / Math.max(1, state!.slideCount)}%` }} />
          </div>
          <div className="live-controls">
            <button onClick={() => void command("prev")}>{t("Wstecz")}</button>
            <button className="primary" onClick={() => void command("next")}>{t("Dalej")}</button>
          </div>

          <div className={`current-slide${state!.isBlank ? " blank" : ""}`}>
            {state!.label && !state!.isVideo && !state!.isKiosk ? <div className="slide-label">{state!.label}</div> : null}
            {state!.isBlank ? <div className="blank-note">{t("Sala widzi czarny ekran")}</div> : null}
            {state!.isVideo ? <VideoControls video={video} /> : null}
            {kioskShown ? (
              <div className="kiosk">
                {kiosk!.image ? <img src={kiosk!.image} alt="" /> : null}
                {kiosk!.title ? <div className="kiosk-title">{kiosk!.title}</div> : null}
                {kiosk!.text ? <p>{kiosk!.text}</p> : null}
                <div className="muted small">{t("Element {0} z {1}", kiosk!.index + 1, kiosk!.count)}{kiosk!.isRunning || kiosk!.count < 2 ? "" : t(", zatrzymany")}</div>
              </div>
            ) : null}
            {state!.image ? <img className="slide-image" src={image ?? undefined} alt="" /> : null}
            {!state!.isVideo && !state!.isKiosk ? <Lines lines={state!.lines} chords={chords} className="live-lines" /> : null}
          </div>

          {state!.nextLines?.length || state!.next ? (
            <div className="upcoming">
              <div className="upcoming-head"><span>{t("Następna")}</span>{state!.next ? <span className="muted">{state!.next}</span> : null}</div>
              {state!.nextLines ? <Lines lines={state!.nextLines} chords={chords} /> : null}
              {state!.nextImage ? <img className="next-image" src={nextImage ?? undefined} alt="" /> : null}
            </div>
          ) : null}

          {state!.slideLabels.length > 1 ? (
            <div className="slide-chips" aria-label={t("Slajdy")}>
              {state!.slideLabels.map((label, i) => (
                <button key={i} className={i === state!.slideIndex ? "active" : ""} aria-current={i === state!.slideIndex}
                        onClick={() => void command("goto", { slide: i })}>{label}</button>
              ))}
            </div>
          ) : null}
        </div>
      )}
    </section>
  );
}

function VideoControls({ video }: { video: Snapshot["video"] }) {
  const [, tick] = useState(0);
  // między wiadomościami (co sekundę) czas grającego filmu płynie lokalnie
  useEffect(() => {
    if (!video?.isPlaying) return;
    const timer = setInterval(() => tick((t) => t + 1), 250);
    return () => clearInterval(timer);
  }, [video?.isPlaying]);

  const v = video ?? { isPlaying: false, position: 0, duration: 0, receivedAt: 0, error: null, pictureOffsetMs: 0 };
  const elapsed = v.isPlaying ? (performance.now() - v.receivedAt) / 1000 : 0;
  const position = Math.min(v.duration || Infinity, v.position + elapsed);
  const ms = v.pictureOffsetMs ?? 0;
  const offset = `${ms > 0 ? "+" : ms < 0 ? "−" : ""}${(Math.abs(ms) / 1000).toFixed(1).replace(".", getLanguage() === "en" ? "." : ",")} s`;
  return (
    <div className="video">
      <div className="video-clock">{formatTime(position)}{v.duration ? <span className="muted"> / {formatTime(v.duration)}</span> : null}</div>
      <div className="video-progress"><div style={{ width: v.duration ? `${(100 * position) / v.duration}%` : "0%" }} /></div>
      <div className="video-buttons">
        <button onClick={() => void command("videoSkip", { seconds: -15 })}>−15 s</button>
        <button className="video-toggle" aria-label={t(v.isPlaying ? "Pauza" : "Odtwórz")} onClick={() => void command("videoToggle")}>
          {v.isPlaying ? <PauseIcon /> : <PlayIcon />}
        </button>
        <button onClick={() => void command("videoSkip", { seconds: 15 })}>+15 s</button>
      </div>
      <div className={`video-offset${ms ? " set" : ""}`}>
        {t("Obraz")}
        <button aria-label={t("Obraz wcześniej o 0,1 s")} onClick={() => void command("videoOffset", { milliseconds: -100 })}>‹</button>
        <b>{offset}</b>
        <button aria-label={t("Obraz później o 0,1 s")} onClick={() => void command("videoOffset", { milliseconds: 100 })}>›</button>
      </div>
      {v.error ? <p className="error">{v.error}</p> : null}
    </div>
  );
}
