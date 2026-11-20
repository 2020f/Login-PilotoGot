"use client";

import { useEffect, useId, useRef, useState } from "react";
import Image from "next/image";
import { Icon } from "./icon";
import { siteUrl } from "@/lib/config";
import { HOME_DEMO_STEPS, homeDemoScene, type HomeDemoMode } from "./home-demo";
import styles from "./home-hero.module.css";

const route = "M350 582 C390 555 413 587 457 573 S565 534 624 504 S730 461 720 426 S704 380 747 363 S826 359 875 330";
const destinations = [{ x: 624, y: 504 }, { x: 720, y: 426 }, { x: 875, y: 330 }];

export function HomeHero() {
  const [mode, setMode] = useState<HomeDemoMode>("simple");
  const [step, setStep] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [reducedMotion, setReducedMotion] = useState(true);
  const [inView, setInView] = useState(false);
  const [visible, setVisible] = useState(true);
  const section = useRef<HTMLElement>(null);
  const scene = useRef<HTMLDivElement>(null);
  const gradient = useId().replace(/:/g, "");
  const state = homeDemoScene(mode, step);
  const running = playing && inView && visible && !reducedMotion;

  useEffect(() => {
    const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const updateMotion = () => { setReducedMotion(preference.matches); if (preference.matches) setPlaying(false); };
    updateMotion();
    setPlaying(!preference.matches);
    preference.addEventListener("change", updateMotion);
    const updateVisibility = () => setVisible(!document.hidden);
    updateVisibility();
    document.addEventListener("visibilitychange", updateVisibility);
    let observer: IntersectionObserver | undefined;
    if (typeof IntersectionObserver === "undefined") setInView(true);
    else {
      observer = new IntersectionObserver(entries => setInView(entries[0]?.isIntersecting || false), { threshold: 0.2 });
      if (scene.current) observer.observe(scene.current);
    }
    return () => { observer?.disconnect(); preference.removeEventListener("change", updateMotion); document.removeEventListener("visibilitychange", updateVisibility); };
  }, []);

  useEffect(() => {
    if (!running) return;
    const timer = window.setTimeout(() => {
      setStep(current => Math.min(current + 1, 4));
      if (step >= 3) setPlaying(false);
    }, 3600);
    return () => window.clearTimeout(timer);
  }, [running, step]);

  function chooseMode(next: HomeDemoMode) { setMode(next); setStep(0); setPlaying(false); }
  function chooseStep(next: number) { setStep(next); setPlaying(false); }
  function togglePlayback() {
    if (step === 4 && !playing) setStep(0);
    setPlaying(current => !current);
  }
  function explore() {
    scene.current?.scrollIntoView({ behavior: reducedMotion ? "auto" : "smooth", block: "center" });
    if (!reducedMotion) { setStep(0); setPlaying(true); }
    else chooseStep(Math.min(step + 1, 4));
  }

  return <section
    className={styles.hero}
    aria-labelledby="home-hero-title"
    ref={section}
    data-motion={running ? "running" : "paused"}
    onFocusCapture={event => { if (event.target instanceof HTMLElement && event.target.matches(":focus-visible")) setPlaying(false); }}
  >
    <div className={styles.halo} aria-hidden="true" />
    <div className={styles.layout}>
      <div className={styles.copy}>
        <span className={styles.kicker}><span aria-hidden="true" />EL RITMO DE TU OPERACIÓN</span>
        <h1 id="home-hero-title"><span>Tu última milla.</span><span className={styles.moving}>En movimiento.<svg viewBox="0 0 440 25" width="440" height="25" aria-hidden="true"><path d="M5 17Q170-4 431 12" /></svg></span></h1>
        <p className={styles.intro}>Del primer pedido a la última parada.<br />Conecta tu negocio, tus pilotos y cada entrega en un mismo recorrido.</p>
        <div className={styles.actions}>
          <a className={styles.primary} href={siteUrl("/planes")}>Ver planes <Icon name="arrow" /></a>
          <button type="button" className={styles.explore} onClick={explore}><span className={styles.playCircle}><svg width="11" height="13" viewBox="0 0 11 13" fill="currentColor" aria-hidden="true"><path d="M1 1.5v10l8-5z" /></svg></span>Explorar el recorrido</button>
        </div>
        <div className={styles.promise}><span><Icon name="building" />Tu negocio</span><i aria-hidden="true" /><span><Icon name="truck" />Tu equipo</span><i aria-hidden="true" /><span><Icon name="route" />Todo conectado</span></div>
      </div>

      <div className={styles.experience} id="home-live-route" ref={scene}>
        <div className={styles.demoHeading}>
          <span className={styles.demoLabel}><span aria-hidden="true" />DEMO INTERACTIVA</span>
          <span className={styles.illustrative}>Escena ilustrativa</span>
        </div>
        <div className={styles.canvas}>
          <div className={styles.orbit} aria-hidden="true" />
          <Image className={styles.city} src="/illustrations/ultima-milla-ciudad.png" alt="Escena ilustrada de un comercio, un piloto y sus destinos de entrega" width={1536} height={1024} priority sizes="(max-width: 800px) 100vw, 60vw" />
          <svg className={styles.route} viewBox="0 0 1000 667" width="1000" height="667" fill="none" aria-hidden="true">
            <defs><linearGradient id={gradient} x1="350" y1="582" x2="875" y2="330" gradientUnits="userSpaceOnUse"><stop stopColor="#27d6ba" /><stop offset="1" stopColor="#007e83" /></linearGradient></defs>
            <path d={route} className={styles.routeBase} />
            <path d={route} pathLength="100" className={styles.routeActive} stroke={`url(#${gradient})`} style={{ strokeDashoffset: 100 - state.progress * 100 }} />
          </svg>
          {(mode === "multiple" ? destinations : destinations.slice(-1)).map((point, index) => <span key={`${mode}-${index}`} className={`${styles.stop} ${index < state.completed ? styles.stopComplete : ""}`} style={{ left: `${point.x / 10}%`, top: `${point.y / 6.67}%` }} aria-hidden="true">{index < state.completed ? <Icon name="check" /> : index + 1}</span>)}
          <div className={styles.pilotDot} style={{ left: `${state.point.x / 10}%`, top: `${state.point.y / 6.67}%` }} aria-hidden="true"><span className={styles.pilotPulse} /><Icon name={step === 4 ? "check" : "truck"} /></div>
          <div className={`${styles.floatingCard} ${styles.orderCard}`}>
            <span className={styles.cardIcon}><Icon name={step >= 2 ? "check" : "box"} /></span><div><small>DESDE TU COMERCIO</small><strong>{step >= 2 ? "Recogida confirmada" : "Un nuevo pedido"}</strong><span>{mode === "multiple" ? "Una orden · tres destinatarios" : "Una orden · un destinatario"}</span></div>
          </div>
          <div className={`${styles.floatingCard} ${styles.pilotCard}`}><span className={styles.pilotAvatar}><Icon name="truck" /></span><div><strong>Un mismo piloto</strong><span>{step === 0 ? "Listo para conectar" : step === 4 ? "Recorrido completado" : "Conectado al recorrido"}</span></div><span className={styles.connectionDot} aria-hidden="true" /></div>
          <div className={`${styles.floatingCard} ${styles.destinationCard}`}><span className={styles.cardIcon}><Icon name="location" /></span><div><strong>{mode === "multiple" ? "Cada destino cuenta." : "El siguiente destino."}</strong><span>{step === 4 ? "Confirmado. Recorrido completo." : mode === "multiple" ? "Tres paradas. Una misma orden." : "Una entrega, de principio a fin."}</span><div className={styles.destinationDots} aria-hidden="true">{Array.from({ length: state.count }, (_, index) => <i className={index < state.completed ? styles.done : ""} key={index} />)}</div></div></div>
        </div>

        <div className={styles.controlPanel}>
          <div className={styles.controlTop}>
            <div className={styles.modes} role="group" aria-label="Tipo de envío de la demostración"><button type="button" aria-pressed={mode === "simple"} onClick={() => chooseMode("simple")}><Icon name="box" />Simple</button><button type="button" aria-pressed={mode === "multiple"} onClick={() => chooseMode("multiple")}><Icon name="route" />Varios destinos</button></div>
            <button type="button" className={styles.playback} onClick={togglePlayback} disabled={reducedMotion} aria-label={playing ? "Pausar demostración" : step === 4 ? "Repetir demostración" : "Reproducir demostración"} title={reducedMotion ? "Usa las etapas: tienes activado reducir movimiento." : undefined}>{playing ? <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true"><path fill="currentColor" d="M4 2h3v12H4zm5 0h3v12H9z" /></svg> : <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true"><path fill="currentColor" d="m5 2 9 6-9 6z" /></svg>}<span>{playing ? "Pausar" : step === 4 ? "Repetir" : "Reproducir"}</span></button>
          </div>
          <ol className={styles.stages} aria-label="Etapas del recorrido ilustrativo">{HOME_DEMO_STEPS.map((label, index) => <li key={label} className={index <= step ? styles.visited : ""}><button type="button" onClick={() => chooseStep(index)} aria-current={index === step ? "step" : undefined} aria-label={`Ver etapa ${index + 1}: ${label}`}><span className={styles.stageDot}>{index < step ? <Icon name="check" /> : index + 1}</span><span>{label}</span></button></li>)}</ol>
          <div className={styles.sceneCopy} aria-live={playing ? "off" : "polite"} aria-atomic="true"><strong>{state.title}</strong><p>{state.description}</p></div>
          {reducedMotion && <small className={styles.reducedHint}>Elige una etapa para explorar el recorrido sin animaciones.</small>}
        </div>
      </div>
    </div>
    <div className={styles.bottomNote}><span className={styles.line} /><Icon name="route" /><span>Una operación que avanza. Un equipo que conecta.</span><span className={styles.line} /></div>
  </section>;
}
