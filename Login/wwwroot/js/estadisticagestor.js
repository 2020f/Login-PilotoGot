(function () {
    const dataEl = document.getElementById("dataPorDia");
    const canvas = document.getElementById("chartPorDia");
    if (!dataEl || !canvas) return;

    let data;
    try { data = JSON.parse(dataEl.textContent || "{}"); }
    catch { data = {}; }

    // 🔒 SANEAR DATA (clave para evitar infinito)
    const safeNums = arr => (arr || []).map(v => {
        const n = Number(v);
        return Number.isFinite(n) ? n : 0;
    });

    const labels = data.labels || [];
    const total = safeNums(data.total);
    const entregadas = safeNums(data.entregadas);
    const incidentes = safeNums(data.incidentes);

    // 🔒 calcular máximo REAL
    const yMax = Math.max(1, ...total, ...entregadas, ...incidentes);

    const ctx = canvas.getContext("2d");

    // 🔒 destruir chart previo si existe (evita duplicados)
    if (window.__chartPorDia) {
        window.__chartPorDia.destroy();
    }

    window.__chartPorDia = new Chart(ctx, {
        type: "line",
        data: {
            labels,
            datasets: [
                {
                    label: "Total",
                    data: total,
                    tension: 0.35,
                    pointRadius: 4,
                    pointHoverRadius: 6
                },
                {
                    label: "Entregadas",
                    data: entregadas,
                    tension: 0.35,
                    pointRadius: 4,
                    pointHoverRadius: 6
                },
                {
                    label: "Incidentes",
                    data: incidentes,
                    tension: 0.35,
                    pointRadius: 4,
                    pointHoverRadius: 6
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false, // 🔥 CLAVE
            plugins: {
                legend: { display: true },
                tooltip: { enabled: true }
            },
            scales: {
                y: {
                    min: 0,              // 🔥 evita negativos
                    max: yMax + 1,       // 🔥 techo fijo
                    ticks: {
                        precision: 0     // 🔥 solo enteros
                    }
                }
            }
        }
    });
})();
