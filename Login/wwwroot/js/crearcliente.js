(function () {
    const backdrop = document.getElementById("saModalCrearCliente");
    if (!backdrop) return;

    // Click fuera => cerrar
    backdrop.addEventListener("click", (e) => {
        if (e.target === backdrop) window.location.href = "/SuperAdmin/Clientes";
    });

    // ESC => cerrar
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") window.location.href = "/SuperAdmin/Clientes";
    });
})();
