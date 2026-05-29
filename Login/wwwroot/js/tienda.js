(function () {
  'use strict';

  /* ── Sidebar toggle ─────────────────────────────────── */
  const sidebar = document.getElementById('tpSidebar');
  const toggleBtn = document.getElementById('tpToggle');
  const STORAGE_KEY = 'tp_sidebar_collapsed';

  function applyCollapsed(collapsed) {
    if (!sidebar) return;
    sidebar.classList.toggle('collapsed', collapsed);
    try { localStorage.setItem(STORAGE_KEY, collapsed ? '1' : '0'); } catch (_) {}
  }

  if (sidebar) {
    const saved = (() => { try { return localStorage.getItem(STORAGE_KEY); } catch (_) { return null; } })();
    if (saved === '1') sidebar.classList.add('collapsed');
  }

  if (toggleBtn) {
    toggleBtn.addEventListener('click', () => {
      applyCollapsed(!sidebar.classList.contains('collapsed'));
    });
  }

  /* ── Modal system ────────────────────────────────────── */
  function openModal(el) {
    if (!el) return;
    el.style.display = 'flex';
    document.body.style.overflow = 'hidden';
  }

  function closeModal(el) {
    if (!el) return;
    el.style.display = 'none';
    document.body.style.overflow = '';
  }

  function closeAllModals() {
    document.querySelectorAll('.tp-modal-backdrop').forEach(closeModal);
  }

  document.addEventListener('click', function (e) {
    // Open
    const openBtn = e.target.closest('[data-tp-open]');
    if (openBtn) {
      const sel = openBtn.getAttribute('data-tp-open');
      openModal(document.querySelector(sel));
      return;
    }

    // Close via button
    const closeBtn = e.target.closest('[data-tp-close]');
    if (closeBtn) {
      const modal = closeBtn.closest('.tp-modal-backdrop');
      closeModal(modal);
      return;
    }

    // Close via backdrop click
    if (e.target.classList.contains('tp-modal-backdrop')) {
      closeModal(e.target);
    }
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
      closeAllModals();
      document.getElementById('pgQrOverlay')?.remove();
    }
  });

  /* ── Search / filter ─────────────────────────────────── */
  const search = document.getElementById('tpSearch');
  const grid = document.getElementById('tpGrid');
  if (search && grid) {
    search.addEventListener('input', function () {
      const q = search.value.toLowerCase().trim();
      grid.querySelectorAll('[data-search]').forEach(c => {
        const match = (c.getAttribute('data-search') || '').includes(q);
        c.style.display = match ? '' : 'none';
      });
    });
  }

  /* ── QR modal (AJAX) ─────────────────────────────────── */
  document.addEventListener('click', async function (e) {
    const link = e.target.closest('[data-open-qr]');
    if (!link) return;

    const ordenId = link.getAttribute('data-orden-id');
    const host = document.getElementById('tpModalHost');
    if (!host) return;

    try {
      const res = await fetch(`/Tienda/QrCodigos?ordenId=${ordenId}`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      });
      if (!res.ok) { alert('No se pudo cargar el modal.'); return; }
      host.innerHTML = await res.text();
    } catch (_) {
      alert('Error de red al cargar el modal.');
    }
  });

  /* ── QR modal close ──────────────────────────────────── */
  document.addEventListener('click', function (e) {
    if (e.target.closest('.qr-close') || e.target.id === 'pgQrOverlay') {
      document.getElementById('pgQrOverlay')?.remove();
    }
  });

})();
