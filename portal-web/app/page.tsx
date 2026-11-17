import { MarketingLayout, ContactCta } from "@/components/site";
import { HomeHero } from "@/components/home-hero";
import { HomeRoleExplorer } from "@/components/home-role-explorer";
import { HomeReveal } from "@/components/home-reveal";
import { Icon } from "@/components/icon";
import { Plans } from "@/components/plans";
import { loadCatalog } from "@/lib/catalog.server";
import { portalUrl } from "@/lib/config";
import styles from "./home.module.css";

export const dynamic = "force-dynamic";
export const metadata = { alternates: { canonical: "/" } };

const questions = [
  ["¿Estoy contratando un servicio de mensajería?", "Envirapido es software para gestionar tu operación de última milla. Tu empresa organiza sus comercios, pilotos y entregas."],
  ["¿Mi cuenta web sirve para entrar al CRM o a la app?", "La cuenta del portal permite consultar tu solicitud, vigencia y descargas autorizadas. Administración entrega por separado las credenciales de la operación."],
  ["¿Cómo se confirma el pago?", "Los pagos se realizan fuera del portal y se validan manualmente. Reportar un pago no lo marca como confirmado ni activa el acceso automáticamente."],
  ["¿Puedo descargar la app desde mi cuenta?", "Sí, cuando administración haya aprobado tu acceso y tu vigencia esté activa, podrás descargar la versión Android privada que se encuentre publicada."]
];

function RouteArtwork() {
  return <div className={styles.routeArtwork} aria-hidden="true">
    <span className={styles.routeSource}><Icon name="building" /></span>
    <svg viewBox="0 0 420 104" fill="none">
      <path d="M28 66H100C139 66 130 28 170 28H237C278 28 259 76 303 76H389" stroke="#d2eae4" strokeWidth="5" />
      <path className={styles.routeTrail} d="M28 66H100C139 66 130 28 170 28H237C278 28 259 76 303 76H389" stroke="#02998f" strokeWidth="3" strokeDasharray="7 9" />
      {[[100, 66], [213, 28], [327, 76]].map(([x, y], i) => <g key={x}><circle cx={x} cy={y} r="17" fill="white" stroke="#04968d" strokeWidth="2" /><text x={x} y={y + 4} fill="#06796f" textAnchor="middle" fontSize="12" fontFamily="Arial" fontWeight="700">{i + 1}</text></g>)}
    </svg>
    <div className={styles.routeLegend}><span>Una recogida</span><span>Varios destinatarios</span></div>
  </div>;
}

export default async function HomePage() {
  const { catalog, unavailable } = await loadCatalog();
  return <MarketingLayout>
    <HomeReveal className={styles.home} progressClassName={styles.readProgress}>
      <HomeHero />
      <div data-home-reveal><HomeRoleExplorer /></div>

      <section className={styles.possibilities} aria-labelledby="home-possibilities">
        <div className="container">
          <div className={styles.sectionHeading} data-home-reveal>
            <div><span className="eyebrow">UNA OPERACIÓN CON MÁS POSIBILIDADES</span><h2 id="home-possibilities">Todo sigue su ruta.<br /><span>Tú conservas el control.</span></h2></div>
            <p>Desde la siguiente parada hasta el cierre del día. Dale a tu equipo herramientas para trabajar conectado.</p>
          </div>
          <div className={styles.featureGrid}>
            <a href="/funciones" className={styles.featureCard + " " + styles.routeCard} data-home-reveal>
              <div className={styles.cardTop}><span className={styles.cardIcon}><Icon name="route" /></span><span className={styles.cardIndex}>01 / RECORRIDOS</span><Icon className={styles.cardArrow} name="arrow" /></div>
              <h3>Una ruta.<br />Muchos destinos.</h3>
              <p>Organiza recogidas y entregas con varias paradas, conservando el seguimiento de cada destinatario.</p>
              <RouteArtwork /><span className={styles.featureLink}>Explora las rutas <Icon name="arrow" /></span>
            </a>
            <a href="/funciones" className={styles.featureCard} data-home-reveal>
              <div className={styles.cardTop}><span className={styles.cardIcon}><Icon name="alert" /></span><span className={styles.cardIndex}>02 / INCIDENCIAS</span><Icon className={styles.cardArrow} name="arrow" /></div>
              <h3>Un imprevisto.<br />El contexto a mano.</h3>
              <p>Reporta incidencias desde la app y atiéndelas con información del pedido o la ruta.</p>
              <div className={styles.incidentArt} aria-hidden="true"><span className={styles.incidentDot} /><div><strong>Incidencia reportada</strong><small>Pedido y ruta conectados</small></div><span className={styles.miniTag}>En atención</span></div>
              <span className={styles.featureLink}>Conoce el seguimiento <Icon name="arrow" /></span>
            </a>
            <a href="/funciones" className={styles.featureCard} data-home-reveal>
              <div className={styles.cardTop}><span className={styles.cardIcon}><Icon name="wallet" /></span><span className={styles.cardIndex}>03 / LIQUIDACIONES</span><Icon className={styles.cardArrow} name="arrow" /></div>
              <h3>Las cuentas claras.<br />Los registros también.</h3>
              <p>Revisa cobros, recepción de efectivo y liquidaciones. Cada confirmación conserva su propio proceso.</p>
              <div className={styles.ledgerArt} aria-hidden="true"><span><Icon name="check" /> Cobros</span><span><Icon name="check" /> Conciliación</span><span><Icon name="check" /> Liquidaciones</span></div>
              <span className={styles.featureLink}>Descubre la gestión <Icon name="arrow" /></span>
            </a>
            <a href="/funciones" className={styles.featureCard + " " + styles.companyCard} data-home-reveal>
              <div className={styles.cardTop}><span className={styles.cardIcon}><Icon name="shield" /></span><span className={styles.cardIndex}>04 / TU EMPRESA</span><Icon className={styles.cardArrow} name="arrow" /></div>
              <h3>Tu equipo conectado.<br />Tu espacio protegido.</h3>
              <p>Cada empresa gestiona sus datos, comercios y pilotos con sus propios accesos y permisos.</p>
              <div className={styles.companyArt} aria-hidden="true"><span><Icon name="building" /> Comercios</span><span className={styles.companyCenter}><Icon name="shield" /></span><span><Icon name="truck" /> Pilotos</span></div>
              <span className={styles.featureLink}>Conoce Envirapido <Icon name="arrow" /></span>
            </a>
          </div>
        </div>
      </section>

      <section className={styles.getStarted} id="como-funciona" aria-labelledby="home-start-title">
        <div className="container">
          <div className={styles.centerHeading} data-home-reveal><span className="eyebrow">EL SIGUIENTE RECORRIDO ES EL TUYO</span><h2 id="home-start-title">De conocerlo<br />a ponerlo en marcha.</h2><p>Un camino claro para comenzar con Envirapido.</p></div>
          <ol className={styles.startSteps}>
            {[
              { number: "01", icon: "layers" as const, title: "Elige tu plan", body: "Encuentra una opción para el tamaño de tu operación.", href: "/planes", action: "Explorar planes" },
              { number: "02", icon: "building" as const, title: "Cuéntanos de ti", body: "Crea tu cuenta web y comparte los datos de tu negocio.", href: portalUrl("/registro"), action: "Crear mi cuenta" },
              { number: "03", icon: "check" as const, title: "Lo revisamos contigo", body: "Administración revisa la solicitud y verifica el pago cuando corresponde.", href: portalUrl("/cuenta/solicitudes"), action: "Ver mi solicitud" },
              { number: "04", icon: "truck" as const, title: "Comienza tu operación", body: "Con tu acceso habilitado, recibe las indicaciones y credenciales de tu equipo.", href: portalUrl("/cuenta/descargas"), action: "Consultar mi acceso" }
            ].map(step => <li key={step.number} data-home-reveal><a href={step.href}><div className={styles.stepTop}><span>{step.number}</span><Icon name={step.icon} /></div><h3>{step.title}</h3><p>{step.body}</p><span className={styles.stepAction}>{step.action} <Icon name="arrow" /></span></a></li>)}
          </ol>
        </div>
      </section>

      <section className={styles.plansSection} id="planes" aria-labelledby="home-plans-title">
        <div className="container">
          <div className={styles.sectionHeading} data-home-reveal><div><span className="eyebrow">ESPACIO PARA LO QUE VIENE</span><h2 id="home-plans-title">Tu operación crece.<br /><span>Encuentra tu plan.</span></h2></div><p>Consulta los precios y límites del catálogo vigente. La activación y los pagos se revisan manualmente.</p></div>
          <div className={styles.plansWrap} data-home-reveal><Plans catalog={catalog} unavailable={unavailable} /></div>
        </div>
      </section>

      <section className={styles.faqSection} aria-labelledby="home-faq-title">
        <div className={"container " + styles.faqGrid}>
          <div className={styles.faqIntro} data-home-reveal><span className="eyebrow">ANTES DE DESPEGAR</span><h2 id="home-faq-title">Buenas preguntas.<br /><span>Respuestas claras.</span></h2><p>Estamos para ayudarte a encontrar la forma de trabajar con Envirapido.</p><a href="/contacto" className="text-link">Hablemos de tu negocio <Icon name="arrow" /></a></div>
          <div className={styles.questions} data-home-reveal>{questions.map(([question, answer], index) => <details key={question}><summary><span className={styles.questionNumber}>0{index + 1}</span><span>{question}</span><span className={styles.questionToggle} aria-hidden="true">+</span></summary><p>{answer}</p></details>)}</div>
        </div>
      </section>
      <div className={styles.endcap} data-home-reveal><ContactCta /></div>
    </HomeReveal>
  </MarketingLayout>;
}
