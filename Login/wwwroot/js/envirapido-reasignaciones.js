(() => {
  'use strict';
  const E = window.Envirapido, source = document.getElementById('evReassignmentData'), holder = document.querySelector('[data-reassignment-content]');
  if (!E || !source || !holder) return;
  let data = JSON.parse(source.textContent), cases = data.gestiones || [];
  const one = (selector, root = document) => root.querySelector(selector);
  const all = (selector, root = document) => Array.from(root.querySelectorAll(selector));
  const field = (form, name) => form.elements.namedItem(name);
  const value = (form, name) => field(form, name)?.value.trim() || '';
  const error = (form, message) => { const box = one('[data-reassign-error]', form); box.className = 'ev-alert red'; box.textContent = message; };
  const record = id => cases.find(item => item.id === Number(id));
  let refreshing = false, mutations = 0, pollTimer = null, refreshController = null;
  const refreshState = one('[data-reassignment-refresh-state]');
  function editing() {
    return document.hidden || mutations > 0 || !!one('dialog[open]') || !!one('[data-reassign-stop]:checked')
      || (document.activeElement instanceof Element && document.activeElement.matches('input, textarea, select, [contenteditable="true"]'));
  }
  function announceSnapshot(updatedAt) {
    holder.dispatchEvent(new CustomEvent('envirapido:scoped-snapshot-applied', { bubbles: true, detail: { scope: 'reasignaciones', updatedAt } }));
  }
  async function refreshSnapshot(manual = false) {
    if (refreshing) return false;
    if (editing()) { if (manual) refreshState.textContent = 'Actualización en pausa: conserva tu selección y termina o cierra la edición para actualizar.'; return false; }
    refreshing = true; refreshController = new AbortController();
    const controller = refreshController, timeout = setTimeout(() => controller.abort(), 10000);
    try {
      const url = new URL('/Operaciones/ReasignacionesFragmento', location.origin);
      if (holder.dataset.orderId) url.searchParams.set('ordenId', holder.dataset.orderId);
      const response = await fetch(url, { credentials: 'same-origin', redirect: 'error', cache: 'no-store', signal: controller.signal, headers: { Accept: 'text/html' } });
      if (!response.ok) throw new Error('No se pudo actualizar la bandeja. La sesión y los intentos pendientes se conservan.');
      const parsed = new DOMParser().parseFromString(await response.text(), 'text/html');
      const fragment = one('[data-reassignment-snapshot]', parsed), payload = fragment && one('#evReassignmentData', fragment);
      if (!fragment || !payload || (fragment.dataset.orderId || '') !== (holder.dataset.orderId || '') || !Number.isFinite(Date.parse(fragment.dataset.updated || '')))
        throw new Error('La actualización recibida no corresponde a esta bandeja.');
      const next = JSON.parse(payload.textContent);
      if (!Array.isArray(next.paradas) || !Array.isArray(next.gestiones)) throw new Error('La actualización de la bandeja no es válida.');
      // Never replace a selection made while this read was in flight.
      if (editing() || !holder.isConnected) return false;
      holder.replaceChildren(document.importNode(fragment, true)); data = next; cases = data.gestiones;
      refreshState.textContent = 'Bandeja actualizada. Se consulta cada 5 segundos mientras no haya edición o selección.';
      announceSnapshot(fragment.dataset.updated); return true;
    } catch (problem) {
      if (!document.hidden && holder.isConnected) refreshState.textContent = problem.name === 'AbortError' ? 'La actualización tardó demasiado; se reintentará sin perder tus formularios.' : problem.message;
      return false;
    } finally { clearTimeout(timeout); refreshing = false; if (refreshController === controller) refreshController = null; }
  }
  async function send(path, body) { mutations++; try { return await E.api(path, body); } finally { mutations--; } }
  async function finished(path, result, form) {
    if (path === 'rutas') { location.assign(`/Operaciones/Rutas?id=${encodeURIComponent(result.id)}`); return; }
    if (/^retiros\/\d+\/ajuste$/.test(path)) {
      form.closest('dialog').close();
      one('[type="submit"]', form).disabled = false;
      const box = one('[data-reassignment-status]'); box.className = 'ev-alert neutral';
      box.replaceChildren(document.createTextNode('Ajuste registrado en revisión. El efectivo y el estado de pago se conservan. Actualiza esta bandeja antes de otra decisión. '));
      const link = document.createElement('a'); link.className = 'ev-btn small'; link.textContent = 'Revisar liquidación y pago externo';
      const id = Number(result?.liquidacionId); link.href = '/Operaciones/Liquidaciones?tab=Repartidores' + (Number.isInteger(id) && id > 0 ? `&id=${id}` : '');
      box.append(link); box.scrollIntoView({ block: 'nearest' }); return;
    }
    form.closest('dialog').close(); one('[type="submit"]', form).disabled = false;
    for (const selected of all('[data-reassign-stop]:checked')) selected.checked = false;
    await refreshSnapshot(true);
  }
  function recovery(form, key) {
    const pending = JSON.parse(sessionStorage.getItem(key) || 'null'); if (!pending) return;
    const box = one('[data-reassign-error]', form), detail = document.createElement('p');
    detail.textContent = `Intento conservado: ${pending.path} · pedido ${pending.payload.ordenId || ''} · paradas ID ${(pending.payload.paradas || []).map(p => p.paradaId).join(', ')}${pending.payload.importe == null ? '' : ` · RD$ ${pending.payload.importe}`} · ${pending.payload.motivo || pending.payload.nota || pending.payload.observaciones || pending.payload.nombre || ''}`;
    const retry = document.createElement('button'); retry.type = 'button'; retry.className = 'ev-btn small'; retry.textContent = 'Consultar / reintentar este mismo intento';
    retry.addEventListener('click', async () => {
      retry.disabled = true;
      try { const result = await send(pending.path, pending.body); sessionStorage.removeItem(key); await finished(pending.path, result, form); }
      catch (problem) { if (problem.solicitudRechazada || [400, 401, 403, 404, 422].includes(problem.status)) sessionStorage.removeItem(key); error(form, problem.message); if (sessionStorage.getItem(key)) recovery(form, key); }
    });
    box.append(detail, retry);
  }
  function open(id, item, action) {
    const dialog = document.getElementById(id), form = one('form', dialog);
    form.reset(); form._reassignment = { item, action };
    const box = one('[data-reassign-error]', form); box.className = ''; box.textContent = '';
    if (id === 'evStopDecision') one('[data-reassign-title]', form).textContent = action === 'Aprobar' ? 'Aprobar retiro de entrega' : 'Rechazar retiro de entrega';
    if (id === 'evStopReceive') one('[data-reassign-receipt-destination]', form).textContent = `Destino: ${item.destinoNombre || ''} · ${item.destinoDireccion || ''}`;
    if (id === 'evRetiroAdjustment') one('[data-reassign-adjustment-pilot]', form).textContent = `Trabajo previo de ${item.pilotoOrigenNombre || `piloto ID ${item.pilotoOrigenId}`} · pedido #${item.codigoA} · parada ID ${item.paradaId}`;
    update(form); dialog.showModal();
  }
  function update(form) {
    if (form.dataset.reassignmentForm === 'selection') {
      const assign = value(form, 'accion') === 'reasignar';
      one('[data-assignment-pilot]', form).hidden = !assign; field(form, 'pilotoId').required = assign;
    }
    if (form.dataset.reassignmentForm === 'return') {
      const center = !value(form, 'tiendaId'); one('[data-return-center]', form).hidden = !center;
      for (const name of ['nombreCentro', 'direccionCentro']) field(form, name).required = center;
    }
  }
  function selection() {
    const selected = all('[data-reassign-stop]:checked');
    if (!selected.length) throw new Error('Selecciona al menos una parada disponible.');
    const parents = new Set(selected.map(input => input.dataset.orderId));
    if (parents.size !== 1) throw new Error('Gestiona una selección de paradas del mismo pedido.');
    return { ordenId: Number(selected[0].dataset.orderId), paradas: selected.map(input => ({ paradaId: Number(input.dataset.reassignStop), revisionAsignacion: input.dataset.revision })) };
  }
  async function searchStops(form) {
    const pilot = Number(value(form, 'pilotoId'));
    const target = field(form, 'paradaIds'); target.replaceChildren();
    if (!pilot) throw new Error('Selecciona primero el piloto de esta ruta.');
    const query = new URLSearchParams({ pilotoId: String(pilot), buscar: value(form, 'buscar') });
    const response = await fetch(`/Operaciones/BuscarParadasRuta?${query}`, { credentials: 'same-origin', cache: 'no-store', redirect: 'error', headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error('No se pudieron consultar las paradas de esta empresa. Actualiza e inténtalo de nuevo.');
    const rows = await response.json();
    // Ignore a late response for a pilot that is no longer selected.
    if (Number(value(form, 'pilotoId')) !== pilot) return;
    for (const row of rows) { const option = new Option(`#${row.codigoA} · Parada ${row.posicion} · ${row.destinatario} · ${row.recibida ? 'Recibida' : 'Por recoger'}`, String(row.paradaId)); option.dataset.orderId = String(row.ordenId); target.add(option); }
    if (!rows.length) error(form, 'No hay paradas disponibles para este piloto y búsqueda. Revisa las recepciones pendientes o cambia la búsqueda.');
  }
  function command(form) {
    const kind = form.dataset.reassignmentForm, context = form._reassignment || {}, item = context.item;
    if (kind === 'selection') {
      const action = value(form, 'accion'), body = { ...selection(), motivo: value(form, 'motivo') };
      if (action === 'reasignar') body.pilotoId = Number(value(form, 'pilotoId'));
      return { path: action, body };
    }
    if (kind === 'decision') return { path: `retiros/${item.id}/decidir`, body: { accion: context.action, nota: value(form, 'nota'), version: item.version } };
    if (kind === 'return') {
      const shop = value(form, 'tiendaId');
      return { path: `retornos/${item.id}`, body: { version: item.version, tiendaId: shop ? Number(shop) : null,
        nombreCentro: shop ? null : value(form, 'nombreCentro'), direccionCentro: shop ? null : value(form, 'direccionCentro'),
        latitud: shop || !value(form, 'latitud') ? null : Number(value(form, 'latitud')),
        longitud: shop || !value(form, 'longitud') ? null : Number(value(form, 'longitud')) } };
    }
    if (kind === 'receive') return { path: `recepciones/${item.id}`, body: { version: item.version, observaciones: value(form, 'observaciones') } };
    if (kind === 'adjust') return { path: `retiros/${item.id}/ajuste`, body: { version: item.version, importe: Number(value(form, 'importe')), motivo: value(form, 'motivo') } };
    const selected = Array.from(field(form, 'paradaIds').selectedOptions);
    if (!selected.length) throw new Error('Consulta y selecciona las paradas disponibles para esta ruta.');
    return { path: 'rutas', body: { nombre: value(form, 'nombre'), fecha: value(form, 'fecha'), pilotoId: Number(value(form, 'pilotoId')),
      ordenIds: [...new Set(selected.map(option => Number(option.dataset.orderId)))], paradaIds: selected.map(option => Number(option.value)),
      zonaId: null, permitirReordenarPiloto: field(form, 'permitirReordenarPiloto').checked } };
  }
  document.addEventListener('click', async event => {
    if (!(event.target instanceof Element)) return;
    const target = event.target;
    const refresh = target.closest('[data-reassign-refresh]'); if (refresh) { await refreshSnapshot(true); return; }
    const opening = target.closest('[data-reassign-open]'); if (opening) open(opening.dataset.reassignOpen);
    const close = target.closest('[data-reassign-close]'); if (close) close.closest('dialog').close();
    const decision = target.closest('[data-reassign-decision]'); if (decision) open('evStopDecision', record(decision.dataset.caseId), decision.dataset.reassignDecision);
    const returning = target.closest('[data-reassign-return]'); if (returning) open('evStopReturn', record(returning.dataset.reassignReturn));
    const receiving = target.closest('[data-reassign-receive]'); if (receiving) open('evStopReceive', record(receiving.dataset.reassignReceive));
    const adjustment = target.closest('[data-reassign-adjust]'); if (adjustment) open('evRetiroAdjustment', record(adjustment.dataset.reassignAdjust));
    const search = target.closest('[data-reassign-search-stops]'); if (search) { search.disabled = true; try { await searchStops(search.closest('form')); } catch (problem) { error(search.closest('form'), problem.message); } finally { search.disabled = false; } }
  });
  for (const form of all('[data-reassignment-form]')) {
    form.addEventListener('change', () => { update(form); });
    if (form.dataset.reassignmentForm === 'route') field(form, 'pilotoId').addEventListener('change', () => field(form, 'paradaIds').replaceChildren());
    form.addEventListener('submit', async event => {
      event.preventDefault(); const button = one('[type="submit"]', form); button.disabled = true;
      let key;
      try {
        const action = command(form);
        key = `envirapido:${document.body.dataset.evUser || ''}:reassignment:${action.path}:${action.body.ordenId || form.dataset.reassignmentForm}`;
        const previous = JSON.parse(sessionStorage.getItem(key) || 'null');
        if (previous && JSON.stringify(previous.payload) !== JSON.stringify(action.body)) throw new Error('Hay un intento pendiente con otros datos. Consulta su resultado antes de crear una gestión diferente; reintentar requiere la misma selección y motivo.');
        const pending = previous || { path: action.path, payload: action.body, body: { ...action.body, operacionId: E.uid() } };
        sessionStorage.setItem(key, JSON.stringify(pending));
        const result = await send(action.path, pending.body); sessionStorage.removeItem(key);
        await finished(action.path, result, form);
      } catch (problem) {
        if (key && (problem.solicitudRechazada || [400, 401, 403, 404, 422].includes(problem.status))) sessionStorage.removeItem(key);
        error(form, problem.message || 'No se pudo confirmar la operación. Conserva el intento y consulta la bandeja.'); if (key && sessionStorage.getItem(key)) recovery(form, key); button.disabled = false;
      }
    });
  }
  function startPolling() { if (pollTimer === null) pollTimer = setInterval(() => { void refreshSnapshot(); }, 5000); void refreshSnapshot(); }
  document.addEventListener('visibilitychange', () => { if (!document.hidden) startPolling(); else refreshController?.abort(); });
  window.addEventListener('pagehide', () => { clearInterval(pollTimer); pollTimer = null; refreshController?.abort(); });
  window.addEventListener('pageshow', startPolling);
  const initial = one('[data-reassignment-snapshot]', holder);
  if (initial) announceSnapshot(initial.dataset.updated);
  startPolling();
})();
