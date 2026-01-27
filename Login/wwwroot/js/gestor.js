(function () {
    // ====== Active menu ======
    const path = window.location.pathname.toLowerCase();
    document.querySelectorAll(".pg-nav-item").forEach(a => {
        const href = (a.getAttribute("href") || "").toLowerCase();
        // asp- helpers generan href final, esto lo detecta igual
        if (href && href !== "javascript:void(0)" && path.startsWith(href)) {
            a.classList.add("pg-active");
        }
    });

    // ====== Asignar Piloto Modal (Ordenes) ======
    const modalEl = document.getElementById("asignarPilotoModal");
    if (!modalEl) return;

    const bsModal = new bootstrap.Modal(modalEl);

    const inputCodigoB = document.getElementById("modalCodigoB");
    const inputPilotoId = document.getElementById("modalPilotoId");
    const modalOrdenLabel = document.getElementById("modalOrdenLabel");

    const search = document.getElementById("pilotSearch");
    const list = document.getElementById("pilotList");

    // abrir modal desde botón
    document.querySelectorAll("[data-open-assign]").forEach(btn => {
        btn.addEventListener("click", () => {
            const codigoB = btn.getAttribute("data-codigob") || "";
            const ordenA = btn.getAttribute("data-ordena") || "";

            inputCodigoB.value = codigoB;
            inputPilotoId.value = "";

            modalOrdenLabel.textContent = ordenA ? `ORD-${ordenA}` : "ORD-????";

            // reset filtro
            if (search) search.value = "";
            filterPilots("");

            bsModal.show();
        });
    });

    // click piloto => set id y submit
    if (list) {
        list.addEventListener("click", (e) => {
            const item = e.target.closest("[data-piloto-id]");
            if (!item) return;

            const id = item.getAttribute("data-piloto-id");
            inputPilotoId.value = id;

            // submit el form del modal
            const form = document.getElementById("asignarPilotoForm");
            if (form) form.submit();
        });
    }

    function filterPilots(term) {
        const t = (term || "").trim().toLowerCase();
        document.querySelectorAll("#pilotList [data-filter]").forEach(row => {
            const hay = (row.getAttribute("data-filter") || "").toLowerCase();
            row.style.display = hay.includes(t) ? "" : "none";
        });
    }

    if (search) {
        search.addEventListener("input", () => filterPilots(search.value));
    }
})();
