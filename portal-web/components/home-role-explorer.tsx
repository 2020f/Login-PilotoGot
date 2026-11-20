"use client";

import { useId, useRef, useState, type KeyboardEvent } from "react";
import { Icon, type IconName } from "./icon";
import styles from "./home-role-explorer.module.css";

const roles = [
  {
    key: "comercio",
    label: "Comercio",
    icon: "building",
    eyebrow: "EL PUNTO DE PARTIDA",
    title: "Un pedido claro desde el principio.",
    description: "Una recogida, uno o varios destinatarios. Organiza la información que tu equipo necesita para comenzar.",
    benefits: [
      "Envíos simples o con varios destinos.",
      "Datos de recogida y entrega en un mismo lugar.",
      "Consulta del avance de cada pedido."
    ]
  },
  {
    key: "operaciones",
    label: "Operaciones",
    icon: "layers",
    eyebrow: "EL CENTRO DE TU OPERACIÓN",
    title: "Las piezas encajan en una misma vista.",
    description: "Coordina pedidos, asignaciones y rutas. Cada empresa trabaja con sus propios datos y permisos.",
    benefits: [
      "Asignación manual y ofertas automáticas.",
      "Rutas que agrupan una o varias órdenes.",
      "Paradas y seguimiento para saber qué sigue."
    ]
  },
  {
    key: "piloto",
    label: "Piloto",
    icon: "truck",
    eyebrow: "EL SIGUIENTE PASO, A LA VISTA",
    title: "Cada parada, clara. Cada entrega, registrada.",
    description: "El piloto recibe sus órdenes y consulta el recorrido desde la app, con el detalle de cada recogida y entrega.",
    benefits: [
      "Varias órdenes y paradas en una misma ruta.",
      "Reporte de incidencias desde la app.",
      "Entrega confirmada con el código C del destinatario."
    ]
  }
] as const satisfies readonly {
  key: string;
  label: string;
  icon: IconName;
  eyebrow: string;
  title: string;
  description: string;
  benefits: readonly string[];
}[];

type RoleKey = typeof roles[number]["key"];

function PreviewHeader({ icon, title, subtitle }: { icon: IconName; title: string; subtitle: string }) {
  return <div className={styles.previewHeader}>
    <span className={styles.appMark}><Icon name={icon} /></span>
    <span className={styles.appTitle}><strong>{title}</strong><small>{subtitle}</small></span>
    <span className={styles.headerDots} aria-hidden="true"><i /><i /><i /></span>
  </div>;
}

function CommercePreview() {
  return <div className={styles.previewContent}>
    <div className={styles.previewTitle}><span className={styles.smallLabel}>NUEVO PEDIDO</span><h4>Una recogida.<br />Más de un destino.</h4></div>
    <div className={styles.origin}>
      <span className={styles.originIcon}><Icon name="building" /></span>
      <span><small>Punto de recogida</small><strong>Tu comercio</strong></span>
      <Icon name="check" className={styles.originCheck} />
    </div>
    <div className={styles.destinationList}>
      {["Primer destinatario", "Segundo destinatario"].map((recipient, index) => <div className={styles.destination} key={recipient}>
        <span className={styles.destinationNumber}>{index + 1}</span>
        <span><strong>{recipient}</strong><small>Dirección de entrega</small></span>
        <Icon name="location" />
      </div>)}
    </div>
    <div className={styles.previewFooter}><Icon name="box" /><span>Dos destinos. Un mismo pedido.</span></div>
  </div>;
}

function OperationsPreview() {
  return <div className={styles.previewContent}>
    <div className={styles.operationsHeading}><div className={styles.previewTitle}><span className={styles.smallLabel}>COORDINACIÓN</span><h4>Todo sigue conectado.</h4></div><span className={styles.outlineBadge}><Icon name="route" />Ruta</span></div>
    <div className={styles.assignmentModes}><span>Asignación</span><strong>Manual <span aria-hidden="true">/</span> Automática</strong></div>
    <div className={styles.orderTable}>
      <div className={styles.tableLabels}><span>Pedido</span><span>Piloto</span><span>Estado</span></div>
      <div className={styles.orderRow}>
        <span className={styles.tableOrder}><Icon name="box" /><strong>Dos destinos</strong></span>
        <span className={styles.assignedPilot}><Icon name="user" />Asignado</span>
        <span className={styles.status}>En ruta</span>
      </div>
    </div>
    <div className={styles.routeSummary}><span className={styles.smallLabel}>UN PILOTO · VARIAS PARADAS</span><ol>
      <li><span className={styles.routeDot}><Icon name="check" /></span><span>Recogida</span><small>Completada</small></li>
      <li className={styles.currentRouteStop}><span className={styles.routeDot}>1</span><span>Primer destino</span><small>Siguiente</small></li>
      <li><span className={styles.routeDot}>2</span><span>Segundo destino</span><small>Pendiente</small></li>
    </ol></div>
  </div>;
}

function PilotPreview() {
  return <div className={styles.previewContent}>
    <div className={styles.pilotHeading}><div className={styles.previewTitle}><span className={styles.smallLabel}>MI RUTA</span><h4>Una parada a la vez.</h4></div><span className={styles.pilotRouteIcon}><Icon name="route" /></span></div>
    <ol className={styles.pilotStops}>
      <li className={styles.completedStop}><span className={styles.stopMarker}><Icon name="check" /></span><span><strong>Recogida en el comercio</strong><small>Recogida confirmada</small></span><Icon name="check" className={styles.completionCheck} /></li>
      <li className={styles.activeStop}><span className={styles.stopMarker}><Icon name="location" /></span><span><small className={styles.nextLabel}>PRÓXIMA PARADA</small><strong>Primer destinatario</strong><small>Detalle de la entrega en la app</small></span><Icon name="arrow" /></li>
      <li><span className={styles.stopMarker}>2</span><span><strong>Segundo destinatario</strong><small>Pendiente de entrega</small></span></li>
    </ol>
    <div className={styles.confirmation}><span className={styles.confirmationIcon}><Icon name="shield" /></span><span><strong>Código C del destinatario</strong><small>Para confirmar su entrega</small></span><span className={styles.codeDots} aria-hidden="true"><i /><i /><i /><i /></span></div>
  </div>;
}

function RolePreview({ role }: { role: RoleKey }) {
  return <div className={styles.previewArea}>
    <div className={styles.previewContext}><span className={styles.contextLine} aria-hidden="true" /><span>VISTA ILUSTRATIVA</span><span className={styles.contextLine} aria-hidden="true" /></div>
    <div className={styles.preview} role="group" aria-label={`Vista ilustrativa: ${role === "operaciones" ? "centro de operaciones" : role}`}>
      <PreviewHeader icon={role === "comercio" ? "building" : role === "operaciones" ? "layers" : "truck"}
        title={role === "comercio" ? "Espacio del comercio" : role === "operaciones" ? "Centro de operaciones" : "App del piloto"}
        subtitle="Envirapido" />
      {role === "comercio" ? <CommercePreview /> : role === "operaciones" ? <OperationsPreview /> : <PilotPreview />}
    </div>
    <p className={styles.previewNote}>La misma entrega, desde el espacio de cada equipo.</p>
  </div>;
}

export function HomeRoleExplorer() {
  const id = useId();
  const [selected, setSelected] = useState(0);
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);

  function navigateTabs(event: KeyboardEvent<HTMLButtonElement>, current: number) {
    if (event.altKey || event.ctrlKey || event.metaKey) return;
    let next: number;
    switch (event.key) {
      case "ArrowRight": next = (current + 1) % roles.length; break;
      case "ArrowLeft": next = (current - 1 + roles.length) % roles.length; break;
      case "Home": next = 0; break;
      case "End": next = roles.length - 1; break;
      default: return;
    }
    event.preventDefault();
    setSelected(next);
    tabs.current[next]?.focus();
  }

  return <section className={styles.section} aria-labelledby={`${id}-heading`}>
    <div className={styles.inner}>
      <div className={styles.heading}>
        <span className={styles.eyebrow}><span aria-hidden="true" />UNA OPERACIÓN, TRES PERSPECTIVAS</span>
        <h2 id={`${id}-heading`}>Un pedido. Tres equipos.<br /><span>Todo conectado.</span></h2>
        <p>Cada persona tiene su espacio. El trabajo avanza en la misma dirección.</p>
      </div>
      <div className={styles.tabs} role="tablist" aria-label="Explora el espacio de cada equipo" aria-orientation="horizontal">
        {roles.map((role, index) => <button key={role.key} id={`${id}-tab-${role.key}`} type="button" role="tab"
          ref={element => { tabs.current[index] = element; }} aria-selected={selected === index}
          aria-controls={`${id}-panel-${role.key}`} tabIndex={selected === index ? 0 : -1}
          className={styles.tab} onClick={() => setSelected(index)} onKeyDown={event => navigateTabs(event, index)}>
          <span className={styles.tabIcon}><Icon name={role.icon} /></span><span>{role.label}</span>
        </button>)}
      </div>
      <div className={styles.panels}>
        {roles.map((role, index) => <div key={role.key} id={`${id}-panel-${role.key}`} role="tabpanel"
          aria-labelledby={`${id}-tab-${role.key}`} hidden={selected !== index} tabIndex={0} className={styles.panel}>
          <div className={styles.copy}>
            <span className={styles.copyEyebrow}>{role.eyebrow}</span>
            <h3>{role.title}</h3>
            <p>{role.description}</p>
            <ul className={styles.benefits}>{role.benefits.map(benefit => <li key={benefit}><span><Icon name="check" /></span>{benefit}</li>)}</ul>
            <a className={styles.moreLink} href="/funciones">Explorar las funciones <Icon name="arrow" /></a>
          </div>
          <RolePreview role={role.key} />
        </div>)}
      </div>
    </div>
  </section>;
}
