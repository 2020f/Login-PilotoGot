(function () {

    // ====== Sidebar toggle ======
    const sidebar = document.getElementById("pgSidebar");
    const toggle  = document.getElementById("pgToggle");
    const PG_KEY  = "pg_sidebar_collapsed";

    if (sidebar && toggle) {
        if (localStorage.getItem(PG_KEY) === "1") {
            sidebar.classList.add("collapsed");
        }

        toggle.addEventListener("click", () => {
            sidebar.classList.toggle("collapsed");
            localStorage.setItem(PG_KEY, sidebar.classList.contains("collapsed") ? "1" : "0");
        });
    }

    // ====== Active nav highlight ======
    const path = window.location.pathname.toLowerCase();
    document.querySelectorAll(".pg-nav-item[href]").forEach(a => {
        const href = (a.getAttribute("href") || "").toLowerCase();
        if (href && href !== "javascript:void(0)" && path.startsWith(href)) {
            a.classList.add("pg-active");
        }
    });

    // ====== Custom pilot assign modal ======
    const overlay   = document.getElementById("pgAssignOverlay");
    if (!overlay) return;

    const inputCodigoB    = document.getElementById("modalCodigoB");
    const inputPilotoId   = document.getElementById("modalPilotoId");
    const modalOrdenLabel = document.getElementById("modalOrdenLabel");
    const pilotSearch     = document.getElementById("pilotSearch");
    const pilotList       = document.getElementById("pilotList");
    const closeBtn        = document.getElementById("pgAssignClose");

    function openModal() { overlay.classList.add("open"); }
    function closeModal() { overlay.classList.remove("open"); }

    // Open on assign buttons
    document.querySelectorAll("[data-open-assign]").forEach(btn => {
        btn.addEventListener("click", () => {
            const codigoB = btn.getAttribute("data-codigob") || "";
            const ordenA  = btn.getAttribute("data-ordena") || "";

            if (inputCodigoB)  inputCodigoB.value  = codigoB;
            if (inputPilotoId) inputPilotoId.value  = "";
            if (modalOrdenLabel) modalOrdenLabel.textContent = ordenA ? `ORD-${ordenA}` : "ORD-????";

            if (pilotSearch) pilotSearch.value = "";
            filterPilots("");

            openModal();
        });
    });

    // Close button
    if (closeBtn) closeBtn.addEventListener("click", closeModal);

    // Close on backdrop click
    overlay.addEventListener("click", e => {
        if (e.target === overlay) closeModal();
    });

    // Close on Escape
    document.addEventListener("keydown", e => {
        if (e.key === "Escape" && overlay.classList.contains("open")) closeModal();
    });

    // Pilot pick → submit
    if (pilotList) {
        pilotList.addEventListener("click", e => {
            const item = e.target.closest("[data-piloto-id]");
            if (!item) return;
            if (inputPilotoId) inputPilotoId.value = item.getAttribute("data-piloto-id");
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

    if (pilotSearch) {
        pilotSearch.addEventListener("input", () => filterPilots(pilotSearch.value));
    }

})();


// ====== Auto-submit filter form (Ordenes) ======
(function () {
    const filtrosForm = document.querySelector(".pg-filters form");
    if (!filtrosForm) return;

    filtrosForm.querySelectorAll("select.pg-filter-select").forEach(s => {
        s.addEventListener("change", () => filtrosForm.submit());
    });

    const numInput  = filtrosForm.querySelector(".pg-search-block input");
    const dateInput = filtrosForm.querySelector('input[type="date"]');

    let debounce = null;
    if (numInput) {
        numInput.addEventListener("input", () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => filtrosForm.submit(), 450);
        });
    }

    if (dateInput) {
        dateInput.addEventListener("change", () => filtrosForm.submit());
    }
})();
