"use client";
import { Button, Notice } from "@/components/ui";
export default function ErrorPage({ reset }: { reset: () => void }) { return <main id="contenido" className="container error-page"><Notice tone="warning" title="No pudimos cargar esta página."><p>Vuelve a intentarlo o utiliza la sección de contacto para recibir ayuda.</p></Notice><Button onClick={reset}>Volver a intentar</Button><a href="/contacto">Contactar a Envirapido</a></main>; }
