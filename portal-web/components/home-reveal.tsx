"use client";

import { useEffect, useRef, type ReactNode } from "react";

export function HomeReveal({ children, className, progressClassName }: { children: ReactNode; className: string; progressClassName: string }) {
  const root = useRef<HTMLDivElement>(null);
  const progress = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const element = root.current;
    if (!element) return;
    const items = Array.from(element.querySelectorAll<HTMLElement>("[data-home-reveal]"));
    const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    let observer: IntersectionObserver | undefined;
    let frame: number | null = null;
    const show = (item: HTMLElement) => { item.dataset.homeEnter = "visible"; observer?.unobserve(item); };
    if (!preference.matches && "IntersectionObserver" in window) {
      observer = new IntersectionObserver(entries => {
        for (const entry of entries) if (entry.isIntersecting) show(entry.target as HTMLElement);
      }, { threshold: 0.06, rootMargin: "0px 0px 30px 0px" });
      for (const item of items) {
        if (item.getBoundingClientRect().top < window.innerHeight) show(item);
        else { item.dataset.homeEnter = "pending"; observer.observe(item); }
      }
    }
    const updateProgress = () => {
      frame = null;
      const available = document.documentElement.scrollHeight - window.innerHeight;
      const ratio = available > 0 ? Math.min(1, Math.max(0, window.scrollY / available)) : 0;
      if (progress.current) progress.current.style.transform = `scaleX(${ratio})`;
    };
    const schedule = () => { if (frame === null) frame = window.requestAnimationFrame(updateProgress); };
    const reduce = () => { if (preference.matches) { items.forEach(show); observer?.disconnect(); } };
    const focus = (event: FocusEvent) => {
      if (event.target instanceof Element) {
        const item = event.target.closest<HTMLElement>("[data-home-reveal]");
        if (item && element.contains(item)) show(item);
      }
    };
    preference.addEventListener("change", reduce);
    element.addEventListener("focusin", focus);
    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule, { passive: true });
    updateProgress();
    return () => {
      observer?.disconnect();
      if (frame !== null) window.cancelAnimationFrame(frame);
      preference.removeEventListener("change", reduce);
      element.removeEventListener("focusin", focus);
      window.removeEventListener("scroll", schedule);
      window.removeEventListener("resize", schedule);
    };
  }, []);
  return <div className={className} ref={root}><div ref={progress} className={progressClassName} aria-hidden="true" />{children}</div>;
}
