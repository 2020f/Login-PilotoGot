(function () {
    'use strict';
    const path = window.location.pathname.toLowerCase();
    document.querySelectorAll('.sa-nav-item').forEach(a => {
        if (path === (a.getAttribute('href') || '').toLowerCase()) a.classList.add('active');
    });
    const toggle = document.getElementById('saMenuToggle');
    const close = document.getElementById('saMenuClose');
    const sidebar = document.getElementById('saSidebar');
    const backdrop = document.getElementById('saMenuBackdrop');
    const main = document.getElementById('saMain');
    if (!toggle || !close || !sidebar || !backdrop || !main) return;
    const mobile = window.matchMedia('(max-width: 900px)');
    let open = false;
    function setOpen(value, restoreFocus) {
        open = mobile.matches && value;
        document.body.classList.toggle('sa-menu-open', open);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        toggle.setAttribute('aria-label', open ? 'Cerrar menú' : 'Abrir menú');
        backdrop.hidden = !open;
        sidebar.inert = mobile.matches && !open;
        main.inert = open;
        if (mobile.matches) sidebar.setAttribute('aria-hidden', open ? 'false' : 'true');
        else sidebar.removeAttribute('aria-hidden');
        if (open) close.focus();
        else if (restoreFocus && mobile.matches) toggle.focus();
    }
    toggle.addEventListener('click', () => setOpen(!open, true));
    close.addEventListener('click', () => setOpen(false, true));
    backdrop.addEventListener('click', () => setOpen(false, true));
    sidebar.addEventListener('click', event => {
        if (event.target.closest('a[href]')) setOpen(false, false);
    });
    document.addEventListener('keydown', event => {
        if (!open) return;
        if (event.key === 'Escape') { event.preventDefault(); setOpen(false, true); return; }
        if (event.key !== 'Tab') return;
        const items = Array.from(sidebar.querySelectorAll('a[href], button:not([disabled]), input:not([type="hidden"]), [tabindex]:not([tabindex="-1"])'))
            .filter(item => item.offsetParent !== null);
        if (!items.length) return;
        const first = items[0], last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    });
    mobile.addEventListener('change', () => setOpen(false, false));
    setOpen(false, false);
})();
