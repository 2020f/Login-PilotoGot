(function () {
    // Marca activo por URL si hace falta (extra)
    const path = (window.location.pathname || "").toLowerCase();
    document.querySelectorAll(".sa-nav-item").forEach(a => {
        const href = (a.getAttribute("href") || "").toLowerCase();
        if (href && path.startsWith(href)) a.classList.add("active");
    });

    // Búsqueda global (placeholder, luego conectamos)
    const input = document.getElementById("saGlobalSearch");
    if (input) {
        input.addEventListener("keydown", (e) => {
            if (e.key === "Enter") {
                // luego: redirigir a una búsqueda real
                console.log("Buscar:", input.value);
            }
        });
    }
})();
