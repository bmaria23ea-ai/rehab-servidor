// Ficha de retroalimentación: marca en rojo lo que falta y avisa junto a Guardar.
document.querySelectorAll("form.ficha-rv").forEach(f => {
    const aviso = f.querySelector(".med-aviso[hidden]");
    const otro = f.querySelector(".fr-otro");
    f.noValidate = true;

    const sinMarcar = g => !g.querySelector("input[type=radio]:checked");
    const esOtro = () => f.querySelector("input[name=respuesta]:checked")?.value === "otro";

    const actualizarOtro = () => {
        otro.disabled = !esOtro();
        if (otro.disabled) otro.classList.remove("med-mal");
    };

    const revisar = () => {
        const errores = [];
        f.querySelectorAll(".fr-grupo, .fr-dim tbody tr[data-nombre]").forEach(g => {
            const falta = sinMarcar(g);
            g.classList.toggle("fr-mal", falta);
            if (falta) errores.push(`${g.dataset.nombre}: falta`);
        });
        const sinTexto = esOtro() && otro.value.trim() === "";
        otro.classList.toggle("med-mal", sinTexto);
        if (sinTexto) errores.push("Otro: falta dato");
        return errores;
    };

    const mostrar = errores => {
        aviso.replaceChildren();
        aviso.hidden = errores.length === 0;
        if (aviso.hidden) return;
        const t = document.createElement("strong");
        t.textContent = "Revise los valores";
        const ul = document.createElement("ul");
        errores.forEach(e => { const li = document.createElement("li"); li.textContent = e; ul.append(li); });
        aviso.append(t, ul);
    };

    f.addEventListener("submit", e => {
        if (e.submitter?.hasAttribute("formnovalidate")) return;
        const errores = revisar();
        if (errores.length === 0) return;
        e.preventDefault();
        f.querySelectorAll(".med-ok, p.med-aviso").forEach(p => p.remove());
        mostrar(errores);
        aviso.scrollIntoView({ block: "center", behavior: "smooth" });
        aviso.focus({ preventScroll: true });
    });

    f.addEventListener("change", () => { actualizarOtro(); if (!aviso.hidden) mostrar(revisar()); });
    f.addEventListener("input", () => { if (!aviso.hidden) mostrar(revisar()); });
    actualizarOtro();
});
