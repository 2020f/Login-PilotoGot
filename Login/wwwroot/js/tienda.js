(function () {
    // Helper modal (lo usaremos en parciales)
    window.tpModal = {
        open: (id) => {
            const el = document.getElementById(id);
            if (!el) return;
            el.style.display = "flex";
            document.body.style.overflow = "hidden";
        },
        close: (id) => {
            const el = document.getElementById(id);
            if (!el) return;
            el.style.display = "none";
            document.body.style.overflow = "";
        }
    };

    // Cerrar por data-tp-close
    document.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-tp-close]");
        if (!btn) return;

        const modalId = btn.getAttribute("data-tp-close");
        if (modalId) window.tpModal.close(modalId);
    });
})();


(function () {
    // ===== Modales =====
    function openModal(sel) {
        const el = document.querySelector(sel);
        if (!el) return;
        el.style.display = "grid";
        document.body.style.overflow = "hidden";
    }

    function closeModal(el) {
        if (!el) return;
        el.style.display = "none";
        document.body.style.overflow = "";
    }

    document.addEventListener("click", function (e) {
        const openBtn = e.target.closest("[data-tp-open]");
        if (openBtn) {
            const sel = openBtn.getAttribute("data-tp-open");
            openModal(sel);
            return;
        }

        const closeBtn = e.target.closest("[data-tp-close]");
        if (closeBtn) {
            const modal = closeBtn.closest(".tp-modal-backdrop");
            closeModal(modal);
            return;
        }

        // click fuera del modal -> cerrar
        const backdrop = e.target.classList.contains("tp-modal-backdrop") ? e.target : null;
        if (backdrop) closeModal(backdrop);
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            document.querySelectorAll(".tp-modal-backdrop").forEach(m => closeModal(m));
        }
    });

    // ===== Buscador Usuarios =====
    const search = document.getElementById("tpUsuariosSearch");
    const grid = document.getElementById("tpUsuariosGrid");

    if (search && grid) {
        search.addEventListener("input", function () {
            const q = (search.value || "").toLowerCase().trim();
            const cards = grid.querySelectorAll("[data-search]");
            let shown = 0;

            cards.forEach(c => {
                const hay = (c.getAttribute("data-search") || "");
                const ok = hay.includes(q);
                c.style.display = ok ? "" : "none";
                if (ok) shown++;
            });
        });
    }
})();


document.addEventListener("click", async (e) => {
    const link = e.target.closest("[data-open-qr]");
    if (!link) return;

    const ordenId = link.getAttribute("data-orden-id");
    const host = document.getElementById("tpModalHost");
    if (!host) return;

    const res = await fetch(`/Tienda/QrCodigos?ordenId=${ordenId}`, {
        headers: { "X-Requested-With": "XMLHttpRequest" }
    });

    if (!res.ok) {
        alert("No se pudo abrir el modal.");
        return;
    }

    host.innerHTML = await res.text();
});

document.addEventListener("click", (e) => {
    const close = e.target.closest("[data-tp-close]");
    if (!close) return;

    const modal = e.target.closest("[data-tp-modal]");
    if (modal) modal.remove();
});


document.addEventListener("click", (e) => {
    if (e.target.closest(".qr-close")) {
        document.getElementById("pgQrOverlay")?.remove();
    }

    if (e.target.id === "pgQrOverlay") {
        e.target.remove();
    }
});

document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
        document.getElementById("pgQrOverlay")?.remove();
    }
});
