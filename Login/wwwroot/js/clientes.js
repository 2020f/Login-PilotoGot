(function () {
    const input = document.getElementById("saClientesSearch");
    const table = document.getElementById("saClientesTable");
    const counter = document.getElementById("saClientesCount");

    if (!input || !table) return;

    const rows = Array.from(table.querySelectorAll("tbody tr"));
    const total = rows.length;

    function updateCount(visible) {
        if (!counter) return;
        counter.textContent = `Mostrando ${visible} de ${total} empresas`;
    }

    function filter() {
        const q = (input.value || "").trim().toLowerCase();
        let visible = 0;

        rows.forEach(r => {
            const hay = (r.getAttribute("data-search") || "");
            const show = q === "" || hay.includes(q);
            r.style.display = show ? "" : "none";
            if (show) visible++;
        });

        updateCount(visible);
    }

    input.addEventListener("input", filter);
    updateCount(total);
})();

(function () {
    const modal = document.getElementById("saModalCrearCliente");
    const btnOpen = document.getElementById("btnNuevaEmpresa");
    if (!modal || !btnOpen) return;

    const close = () => (modal.style.display = "none");
    const open = () => (modal.style.display = "flex");

    btnOpen.addEventListener("click", (e) => {
        e.preventDefault();
        open();
    });

    modal.addEventListener("click", (e) => {
        if (e.target === modal) close();
    });

    modal.querySelectorAll("[data-sa-close]").forEach(btn => {
        btn.addEventListener("click", close);
    });

    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") close();
    });
})();

