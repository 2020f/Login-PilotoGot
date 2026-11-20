export type HomeDemoMode = "simple" | "multiple";
export const HOME_DEMO_STEPS = ["Pedido recibido", "Asignado", "Recogida", "En ruta", "Entregado"] as const;
const points = [{ x: 350, y: 582 }, { x: 420, y: 575 }, { x: 545, y: 542 }, { x: 724, y: 443 }, { x: 875, y: 330 }] as const;
const progress = [0.02, 0.15, 0.37, 0.69, 1] as const;
export function homeDemoScene(mode: HomeDemoMode, requestedStep: number) {
  const step = Math.max(0, Math.min(4, Math.trunc(Number.isFinite(requestedStep) ? requestedStep : 0)));
  const count = mode === "multiple" ? 3 : 1;
  const completed = step === 4 ? count : mode === "multiple" && step === 3 ? 1 : 0;
  const messages = [
    { title: "Todo comienza con un pedido.", description: "El comercio prepara el envío y comparte la información de sus entregas." },
    { title: "El piloto entra en escena.", description: "La asignación manual o automática conecta el pedido con su recorrido." },
    { title: "Recogido. Listo para salir.", description: "El piloto confirma la recogida y continúa hacia los destinatarios." },
    { title: "Cada parada tiene su momento.", description: mode === "multiple" ? "Un mismo piloto visita tres destinos dentro de una sola orden." : "El piloto sigue el recorrido hacia el destinatario de la orden." },
    { title: "Una entrega que llega a su destino.", description: mode === "multiple" ? "Las tres entregas confirmadas completan esta escena ilustrativa." : "La entrega confirmada completa esta escena ilustrativa." }
  ];
  return { step, count, completed, point: points[step], progress: progress[step], ...messages[step] };
}
