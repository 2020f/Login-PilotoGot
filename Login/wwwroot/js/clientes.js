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

