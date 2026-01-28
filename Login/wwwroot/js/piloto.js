(function () {
    const btnAbrir = document.getElementById("btnAbrirValidacion");
    const btnCerrar = document.getElementById("btnCerrarModal");
    const modal = document.getElementById("modalValidacion");
    const backdrop = document.getElementById("modalBackdrop");

    if (!btnAbrir || !modal || !backdrop) return;

    // Input normal del Código C (alfanumérico 10-15)
    const txtCodigoC = document.getElementById("txtCodigoC");
    const chkC = document.getElementById("chkC");

    const txtCodigoBFinal = document.getElementById("txtCodigoBFinal");
    const btnScan = document.getElementById("btnHabilitadoScan");
    const btnFinalizar = document.getElementById("btnFinalizarEntrega");

    const formEntrega = document.getElementById("formConfirmarEntrega");
    const hdB = document.getElementById("hdCodigoB");
    const hdC = document.getElementById("hdCodigoC");

    function openModal() {
        modal.classList.add("open");
        backdrop.classList.add("open");
        modal.setAttribute("aria-hidden", "false");
        backdrop.setAttribute("aria-hidden", "false");

        // reset
        if (txtCodigoC) txtCodigoC.value = "";
        setCheck(false);
        lockStep2();
        lockFinalize();

        setTimeout(() => txtCodigoC?.focus(), 60);
    }

    function closeModal() {
        modal.classList.remove("open");
        backdrop.classList.remove("open");
        modal.setAttribute("aria-hidden", "true");
        backdrop.setAttribute("aria-hidden", "true");
    }

    function setCheck(on) {
        if (!chkC) return;
        chkC.style.opacity = on ? "1" : ".2";
    }

    // ✅ Ahora válido: letras + números, entre 10 y 15 caracteres
    function isCodigoCValid() {
        const v = (txtCodigoC?.value || "").trim();
        return /^[A-Za-z0-9]{10,15}$/.test(v);
    }

    function unlockStep2() {
        if (txtCodigoBFinal) txtCodigoBFinal.disabled = false;
        if (btnScan) btnScan.disabled = false;
    }

    function lockStep2() {
        if (txtCodigoBFinal) {
            txtCodigoBFinal.value = "";
            txtCodigoBFinal.disabled = true;
        }
        if (btnScan) btnScan.disabled = true;
    }

    function unlockFinalize() {
        if (btnFinalizar) {
            btnFinalizar.disabled = false;
            btnFinalizar.classList.remove("pg-btn-lock");
            btnFinalizar.classList.add("pg-btn-dark");
            btnFinalizar.textContent = "🔒 Finalizar Entrega";
        }
    }

    function lockFinalize() {
        if (!btnFinalizar) return;
        btnFinalizar.disabled = true;
        btnFinalizar.classList.add("pg-btn-lock");
        btnFinalizar.classList.remove("pg-btn-dark");
    }

    // Abrir / cerrar modal
    btnAbrir.addEventListener("click", openModal);
    btnCerrar?.addEventListener("click", closeModal);
    backdrop.addEventListener("click", closeModal);

    // Código C behavior (alfanumérico, máximo 15)
    txtCodigoC?.addEventListener("input", () => {
        txtCodigoC.value = (txtCodigoC.value || "")
            .replace(/[^A-Za-z0-9]/g, "") // solo letras y números
            .slice(0, 15); // máximo 15

        const ok = isCodigoCValid();
        setCheck(ok);

        if (ok) unlockStep2();
        else {
            lockStep2();
            lockFinalize();
        }
    });

    // "Escanear QR de Cierre"
    btnScan?.addEventListener("click", () => {
        if (!txtCodigoBFinal) return;
        txtCodigoBFinal.focus();
    });

    // Cuando se escribe/pega B final
    txtCodigoBFinal?.addEventListener("input", () => {
        const b = (txtCodigoBFinal.value || "").trim();
        if (b.length > 0 && isCodigoCValid()) unlockFinalize();
        else lockFinalize();
    });

    // Finalizar -> submit real a tu action ConfirmarEntrega
    btnFinalizar?.addEventListener("click", () => {
        const c = (txtCodigoC?.value || "").trim();
        const b = (txtCodigoBFinal?.value || "").trim();

        if (!/^[A-Za-z0-9]{10,15}$/.test(c)) return;
        if (!b) return;

        hdC.value = c;  // CodigoC
        hdB.value = b;  // CodigoB

        formEntrega.submit();
    });
})();
