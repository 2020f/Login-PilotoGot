import { MarketingLayout } from "@/components/site";
import { BoxMark } from "@/components/ui";
export default function NotFound() { return <MarketingLayout><section className="section"><div className="container not-found"><BoxMark /><span className="eyebrow">PÁGINA NO ENCONTRADA</span><h1>Volvamos a una buena ruta.</h1><p>La página que buscas no está disponible.</p><a href="/" className="button">Volver al inicio</a></div></section></MarketingLayout>; }
