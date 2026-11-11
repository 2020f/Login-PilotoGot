(function () {
    'use strict';
    const sidebar = document.getElementById('pgSidebar');
    const toggle = document.getElementById('pgToggle');
    if (sidebar && toggle) {
        if (localStorage.getItem('pg_sidebar_collapsed') === '1') sidebar.classList.add('collapsed');
        toggle.addEventListener('click', () => {
            sidebar.classList.toggle('collapsed');
            localStorage.setItem('pg_sidebar_collapsed', sidebar.classList.contains('collapsed') ? '1' : '0');
        });
    }
    document.querySelectorAll('.pg-nav-item[href]').forEach(a => {
        const link = new URL(a.getAttribute('href'), window.location.origin);
        const page = new URL(window.location.href);
        const historyMatches = (link.searchParams.get('historial') === 'true') === (page.searchParams.get('historial') === 'true');
        if (page.pathname.toLowerCase() === link.pathname.toLowerCase() && historyMatches) a.classList.add('pg-active');
    });
    function closeModal() { document.getElementById('pgAssignOverlay')?.classList.remove('open'); }
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-open-assign]');
        const overlay = document.getElementById('pgAssignOverlay');
        if (button && overlay) {
            document.getElementById('modalOrdenId').value = button.dataset.ordenid;
            document.getElementById('modalCodigoB').value = button.dataset.codigob;
            document.getElementById('modalPilotoId').value = '';
            document.getElementById('modalOrdenLabel').textContent = 'ORD-' + button.dataset.ordena;
            document.getElementById('pilotSearch').value = '';
            document.querySelectorAll('#pilotList [data-filter]').forEach(row => row.hidden = false);
            overlay.classList.add('open');
        }
        if (event.target.closest('#pgAssignClose, #pgAssignClose2') || event.target === overlay) closeModal();
        const pilot = event.target.closest('#pilotList [data-piloto-id]');
        if (pilot) {
            document.getElementById('modalPilotoId').value = pilot.dataset.pilotoId;
            document.getElementById('asignarPilotoForm').requestSubmit();
        }
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') closeModal(); });
    document.addEventListener('input', event => {
        if (event.target.id !== 'pilotSearch') return;
        const value = event.target.value.trim().toLowerCase();
        document.querySelectorAll('#pilotList [data-filter]').forEach(row => row.hidden = !row.dataset.filter.toLowerCase().includes(value));
    });
    const form = document.getElementById('pgOrderFilters');
    if (form) {
        const submitFilters = () => {
            form.elements.pagina.value = '1';
            form.requestSubmit();
        };
        form.querySelectorAll('select, input[type="date"]').forEach(field => field.addEventListener('change', submitFilters));
        let debounce;
        form.querySelector('input[name="NumeroOrdenA"]')?.addEventListener('input', () => {
            clearTimeout(debounce); debounce = setTimeout(submitFilters, 450);
        });
    }
    const target = document.getElementById('pgDispatchData');
    const status = document.getElementById('pgSyncStatus');
    if (!target || !form) return;
    let timer, inFlight = false, request, stopped = false;
    const schedule = () => {
        clearTimeout(timer);
        if (!document.hidden && !stopped) timer = setTimeout(refresh, 5000);
    };
    async function refresh() {
        if (inFlight || document.hidden || stopped) return;
        if (document.getElementById('pgAssignOverlay')?.classList.contains('open')) { schedule(); return; }
        inFlight = true;
        request = new AbortController();
        const timeout = setTimeout(() => request.abort(), 20000);
        try {
            const url = new URL(target.dataset.url, window.location.origin);
            url.search = new URLSearchParams(new FormData(form)).toString();
            const result = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: request.signal,
                headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (result.redirected || result.status === 401 || result.status === 403) {
                stopped = true;
                throw new Error('La sesión o el acceso cambió. Recarga la página.');
            }
            if (!result.ok) throw new Error('No se pudo actualizar. Se reintentará.');
            const html = await result.text();
            if (!document.hidden && !document.getElementById('pgAssignOverlay')?.classList.contains('open')) target.innerHTML = html;
            status.textContent = 'Actualizado ' + new Date().toLocaleTimeString('es-DO', { timeZone: 'America/Santo_Domingo' });
        } catch (error) {
            if (!document.hidden) status.textContent = error.name === 'AbortError' ? 'La actualización tardó demasiado. Se reintentará.' : error.message;
        } finally {
            clearTimeout(timeout); request = null; inFlight = false; schedule();
        }
    }
    document.addEventListener('visibilitychange', () => {
        clearTimeout(timer);
        if (document.hidden) request?.abort(); else refresh();
    });
    window.addEventListener('pagehide', () => { stopped = true; clearTimeout(timer); request?.abort(); });
    schedule();
})();
