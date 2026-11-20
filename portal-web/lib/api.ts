export class PortalApiError extends Error {
  readonly status: number;
  readonly code: string;
  constructor(status: number, message: string, code = "REQUEST_FAILED") {
    super(message);
    this.name = "PortalApiError";
    this.status = status;
    this.code = code;
  }
}
export class PortalApi {
  private readonly fetcher: typeof fetch;
  private csrf: string | null = null;
  private csrfRequest: Promise<string> | null = null;
  private csrfEpoch = 0;
  private readonly timeoutMs: number;
  constructor(fetcher: typeof fetch = globalThis.fetch.bind(globalThis), timeoutMs = 20000) { this.fetcher = fetcher; this.timeoutMs = timeoutMs; }
  invalidateCsrf() { this.csrfEpoch++; this.csrf = null; this.csrfRequest = null; }
  private path(path: string): string {
    if (!path.startsWith("/") || path.startsWith("//") || path.includes("..") || path.includes(":") || path.includes("\\") || /[\r\n]/.test(path)) {
      throw new PortalApiError(400, "La ruta solicitada no es válida.", "INVALID_PATH");
    }
    return `/portal-api/v1${path}`;
  }
  private async decode<T>(response: Response): Promise<T> {
    if (response.status === 204) return undefined as T;
    let value: unknown;
    try { value = await response.json(); } catch { throw new PortalApiError(response.ok ? 502 : response.status, "No se pudo interpretar la respuesta del portal. Inténtalo nuevamente.", "INVALID_RESPONSE"); }
    if (!response.ok) {
      const error = value && typeof value === "object" ? value as Record<string, unknown> : {};
      const message = typeof error.error === "string" ? error.error : response.status === 401 ? "Inicia sesión para continuar." : "No se pudo completar la solicitud.";
      throw new PortalApiError(response.status, message, typeof error.code === "string" ? error.code : "REQUEST_FAILED");
    }
    return value as T;
  }
  private async send<T>(path: string, init: RequestInit): Promise<T> {
    const timeout = AbortSignal.timeout(this.timeoutMs);
    const signal = init.signal ? AbortSignal.any([init.signal, timeout]) : timeout;
    try {
      const response = await this.fetcher(this.path(path), {
        ...init,
        credentials: "include",
        cache: "no-store",
        redirect: "error",
        signal,
        headers: { Accept: "application/json", ...init.headers }
      });
      return await this.decode<T>(response);
    } catch (error) {
      if (error instanceof PortalApiError || init.signal?.aborted) throw error;
      throw new PortalApiError(0, "No se recibió una respuesta. Conserva esta pestaña y reintenta la misma solicitud.", "NETWORK_UNCERTAIN");
    }
  }
  async get<T>(path: string, signal?: AbortSignal): Promise<T> { return this.send<T>(path, { method: "GET", signal }); }
  private async token(): Promise<string> {
    if (this.csrf) return this.csrf;
    if (this.csrfRequest) return this.csrfRequest;
    const epoch = this.csrfEpoch;
    const work = this.get<{ requestToken: string }>("/csrf").then(result => {
      if (!result.requestToken) throw new PortalApiError(502, "No se pudo preparar el formulario.", "CSRF_UNAVAILABLE");
      if (epoch !== this.csrfEpoch) return this.token();
      this.csrf = result.requestToken;
      return result.requestToken;
    });
    this.csrfRequest = work;
    try { return await work; } finally { if (this.csrfRequest === work) this.csrfRequest = null; }
  }
  async mutate<T>(path: string, body: unknown, options: { method?: "POST" | "PUT"; idempotencyKey?: string } = {}): Promise<T> {
    const token = await this.token();
    const headers: Record<string, string> = { "Content-Type": "application/json", "X-CSRF-TOKEN": token };
    if (options.idempotencyKey) headers["Idempotency-Key"] = options.idempotencyKey;
    try { return await this.send<T>(path, { method: options.method || "POST", headers, body: JSON.stringify(body) }); }
    catch (error) {
      if (error instanceof PortalApiError && /csrf|antiforgery/i.test(error.code)) this.invalidateCsrf();
      throw error;
    }
  }
}
// No cookie values, tokens or passwords are stored in browser storage.
export const portalApi = new PortalApi();

/** A network retry reuses the exact in-memory payload and UUID. Never log it. */
export class MutationIntent<T> {
  private current: { key: string; body: T; serialized: string } | null = null;
  prepare(body: T): { key: string; body: T } {
    const serialized = JSON.stringify(body);
    if (this.current && this.current.serialized !== serialized) throw new PortalApiError(409, "Hay un intento pendiente. Reintenta con los mismos datos antes de modificarlos.", "PENDING_INTENT");
    if (!this.current) this.current = { key: crypto.randomUUID(), body: structuredClone(body), serialized };
    return { key: this.current.key, body: structuredClone(this.current.body) };
  }
  clear() { this.current = null; }
  get pending() { return this.current !== null; }
}
