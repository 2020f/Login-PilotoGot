import type { SVGProps } from "react";

const paths = {
  arrow: ["M5 12h14", "m13 6 6 6-6 6"],
  check: ["m5 12 4 4L19 6"],
  truck: ["M3 6h11v11H3z", "M14 10h4l3 4v3h-7", "M7 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4", "M18 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4"],
  layers: ["m12 3 10 5-10 5L2 8z", "m2 12 10 5 10-5", "m2 16 10 5 10-5"],
  route: ["M6 7a3 3 0 1 0 0-6 3 3 0 0 0 0 6", "M18 23a3 3 0 1 0 0-6 3 3 0 0 0 0 6", "M6 7v6c0 5 12-7 12-2v6"],
  building: ["M4 21V5h10v16", "M14 10h6v11", "M1 21h22", "M8 9h2m-2 4h2m-2 4h2m7-3h1m-1 4h1"],
  user: ["M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8", "M4 21v-2a8 6 0 0 1 16 0v2z"],
  shield: ["m12 2 8 4v6c0 5-8 10-8 10S4 17 4 12V6z", "m8 12 3 3 5-6"],
  download: ["M12 3v12", "m7 10 5 5 5-5", "M4 15v6h16v-6"],
  external: ["M14 3h7v7", "m10 14 11-11", "M10 3H3v18h18v-7"],
  mail: ["M3 5h18v14H3z", "m3 6 9 7 9-7"],
  phone: ["M6 3h4l1 5-3 2a16 16 0 0 0 6 6l2-3 5 1v4c0 3-4 3-8 1S5 13 3 9 3 3 6 3Z"],
  menu: ["M3 6h18M3 12h18M3 18h18"],
  close: ["m6 6 12 12M6 18 18 6"],
  logout: ["M9 3H3v18h6", "M9 12h12m-4-4 4 4-4 4"],
  clock: ["M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20", "M12 6v6l4 2"],
  help: ["M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20", "M9 8a3 3 0 0 1 6 0c0 3-3 2-3 6", "M12 18h.01"],
  wallet: ["M3 5h17v15H3z", "M3 5V3h15", "M15 10h6v5h-6z"],
  eye: ["M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6"],
  eyeOff: ["m3 3 18 18", "M9 5a12 12 0 0 1 13 7 16 16 0 0 1-4 5", "M6 6a16 16 0 0 0-4 6s4 7 10 7c2 0 4-1 5-2"],
  location: ["M20 10c0 6-8 12-8 12S4 16 4 10a8 8 0 1 1 16 0Z", "M12 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6"],
  alert: ["m12 2 11 20H1z", "M12 9v5m0 3h.01"],
  refresh: ["M21 4v6h-6", "M3 20v-6h6", "M5 8a8 8 0 0 1 13-4l3 6M3 14l3 6a8 8 0 0 0 13-4"],
  box: ["m12 2 10 5v10l-10 5L2 17V7z", "m2 7 10 5 10-5M12 12v10M7 4l10 5"],
  lock: ["M5 10h14v12H5z", "M8 10V6a4 4 0 0 1 8 0v4"],
  info: ["M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20", "M12 11v6m0-10h.01"]
} as const;
export type IconName = keyof typeof paths;
export function Icon({ name, className = "", width = 21, height = 21, ...props }: SVGProps<SVGSVGElement> & { name: IconName }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props} className={`icon ${className}`.trim()} width={width} height={height} aria-hidden="true" focusable="false">{paths[name].map((path, index) => <path d={path} key={index} />)}</svg>;
}
