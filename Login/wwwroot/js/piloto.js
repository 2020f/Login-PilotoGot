(function () {
    'use strict';

    /* ── Validation modal (C + B) ─────────────────────── */
    const btnAbrir = document.getElementById('btnAbrirValidacion');
    const btnCerrar = document.getElementById('btnCerrarModal');
    const modal = document.getElementById('modalValidacion');
    const backdrop = document.getElementById('modalBackdrop');

    const txtCodigoC = document.getElementById('txtCodigoC');
    const chkC = document.getElementById('chkC');
    const txtCodigoBFinal = document.getElementById('txtCodigoBFinal');
    const btnFinalizar = document.getElementById('btnFinalizarEntrega');

    const formEntrega = document.getElementById('formConfirmarEntrega');
    const hdB = document.getElementById('hdCodigoB');
    const hdC = document.getElementById('hdCodigoC');

    function openModal() {
        if (!modal || !backdrop) return;
        modal.classList.add('open');
        backdrop.classList.add('open');
        modal.setAttribute('aria-hidden', 'false');
        backdrop.setAttribute('aria-hidden', 'false');
        resetModal();
        setTimeout(() => txtCodigoC && txtCodigoC.focus(), 60);
    }

    function closeModal() {
        if (!modal || !backdrop) return;
        modal.classList.remove('open');
        backdrop.classList.remove('open');
        modal.setAttribute('aria-hidden', 'true');
        backdrop.setAttribute('aria-hidden', 'true');
    }

    function resetModal() {
        if (txtCodigoC) txtCodigoC.value = '';
        setCheck(false);
        lockFinalize();
    }

    function setCheck(on) {
        if (chkC) chkC.style.opacity = on ? '1' : '.2';
    }

    function isCodigoCValid() {
        const v = (txtCodigoC && txtCodigoC.value || '').trim();
        return /^[A-Za-z0-9]{10,15}$/.test(v);
    }

    function isCodigoBValid() {
        const b = (txtCodigoBFinal && txtCodigoBFinal.value || '').trim();
        return b.length > 0;
    }

    function unlockFinalize() {
        if (!btnFinalizar) return;
        btnFinalizar.disabled = false;
    }

    function lockFinalize() {
        if (!btnFinalizar) return;
        btnFinalizar.disabled = true;
    }

    if (btnAbrir) btnAbrir.addEventListener('click', openModal);
    if (btnCerrar) btnCerrar.addEventListener('click', closeModal);
    if (backdrop) backdrop.addEventListener('click', closeModal);

    if (txtCodigoC) {
        txtCodigoC.addEventListener('input', function () {
            this.value = (this.value || '').replace(/[^A-Za-z0-9]/g, '').slice(0, 15);
            const ok = isCodigoCValid();
            setCheck(ok);
            if (ok && isCodigoBValid()) unlockFinalize();
            else lockFinalize();
        });
    }

    if (txtCodigoBFinal) {
        txtCodigoBFinal.addEventListener('input', function () {
            this.value = (this.value || '').replace(/\s+/g, '');
            if (isCodigoCValid() && isCodigoBValid()) unlockFinalize();
            else lockFinalize();
        });
    }

    if (btnFinalizar) {
        btnFinalizar.addEventListener('click', function () {
            const c = (txtCodigoC && txtCodigoC.value || '').trim();
            const b = (txtCodigoBFinal && txtCodigoBFinal.value || '').trim();
            if (!/^[A-Za-z0-9]{10,15}$/.test(c)) return;
            if (!b) return;
            hdC.value = c;
            hdB.value = b;
            formEntrega.submit();
        });
    }

    /* ── Navigation chooser (Navegar) ──────────────────── */
    const navOverlay = document.getElementById('navOverlay');
    const navSheet = document.getElementById('navSheet');
    let navTarget = null;

    function parseLatLng(link) {
        if (!link) return null;
        try {
            const u = new URL(link);
            const q = u.searchParams.get('q');
            if (q && /^-?\d+(\.\d+)?\s*,\s*-?\d+(\.\d+)?$/.test(q.trim())) {
                return q.trim();
            }
        } catch (_) { /* ignore */ }
        return null;
    }

    function openNavSheet(btn) {
        navTarget = {
            link: btn.getAttribute('data-nav-link') || '',
            query: btn.getAttribute('data-nav-query') || ''
        };
        if (navSheet) navSheet.classList.add('open');
        if (navOverlay) navOverlay.classList.add('open');
    }

    function closeNavSheet() {
        if (navSheet) navSheet.classList.remove('open');
        if (navOverlay) navOverlay.classList.remove('open');
        navTarget = null;
    }

    document.addEventListener('click', function (e) {
        const navBtn = e.target.closest('[data-nav]');
        if (navBtn) {
            openNavSheet(navBtn);
            return;
        }
        if (e.target.closest('[data-gps]')) {
            const gps = e.target.closest('[data-gps]').getAttribute('data-gps');
            if (navTarget) navigate(gps, navTarget);
            closeNavSheet();
            return;
        }
        if (e.target === navOverlay) {
            closeNavSheet();
        }
    });

    function navigate(gps, target) {
        const latlng = parseLatLng(target.link);
        const query = target.query || target.link;

        let url = '';

        if (gps === 'google') {
            url = latlng
                ? 'https://www.google.com/maps/dir/?api=1&destination=' + encodeURIComponent(latlng)
                : 'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(query);
        } else if (gps === 'waze') {
            url = latlng
                ? 'https://waze.com/ul?ll=' + encodeURIComponent(latlng) + '&navigate=yes'
                : 'https://waze.com/ul?q=' + encodeURIComponent(query) + '&navigate=yes';
        } else if (gps === 'apple') {
            url = latlng
                ? 'https://maps.apple.com/?daddr=' + encodeURIComponent(latlng)
                : 'https://maps.apple.com/?q=' + encodeURIComponent(query);
        }

        if (url) window.location.href = url;
    }
})();
