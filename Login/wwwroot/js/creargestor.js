(function () {
    const backdrop = document.getElementById("saModalGestor");
    if (!backdrop) return;

    // Cerrar al clickear fuera del modal
    backdrop.addEventListener("click", (e) => {
        if (e.target === backdrop) {
            window.location.href = "/SuperAdmin/Clientes";
        }
    });

    // Cerrar con ESC
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") {
            window.location.href = "/SuperAdmin/Clientes";
        }
    });
})();
