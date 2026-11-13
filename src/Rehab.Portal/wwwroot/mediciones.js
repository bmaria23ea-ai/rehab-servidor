// Mediciones: casillas en rojo y avisos cortos junto a Guardar.
document.querySelectorAll("form.mediciones").forEach(f => {
    const aviso = f.querySelector(".med-aviso");
    const nombres = { fc: "FC", spo2: "SpO₂", borg: "Borg", pas: "PA", pad: "PA" };
    f.noValidate = true;

    const problema = i => {
        const v = i.validity;
        const decimal = i.step === "0.5";
        if (v.badInput || v.rangeUnderflow || v.rangeOverflow || (decimal && v.stepMismatch))
            return `${i.min} a ${i.max}` + (decimal ? ", de 0,5 en 0,5" : "");
        return v.stepMismatch ? "sin decimales" : "";
    };

    const revisar = () => {
        const errores = new Set();
        f.querySelectorAll("tbody tr").forEach(tr => {
            const momento = tr.querySelector("th").textContent.trim();
            const msj = new Map();
            tr.querySelectorAll("input").forEach(i => { const m = problema(i); if (m) msj.set(i, m); });
            const pa = tr.querySelectorAll(".med-pa input");
            if (pa.length === 2 && !msj.has(pa[0]) && !msj.has(pa[1])) {
                const [s, d] = pa;
                if ((s.value === "") !== (d.value === "")) msj.set(s.value === "" ? s : d, "falta dato");
                else if (s.value !== "" && +s.value <= +d.value) msj.set(s, "sistólica mayor que diastólica");
            }
            tr.querySelectorAll("input").forEach(i => {
                const m = msj.get(i);
                i.classList.toggle("med-mal", !!m);
                i.toggleAttribute("aria-invalid", !!m);
                if (m) errores.add(`${momento} · ${nombres[i.name.split("_").pop()]}: ${m}`);
            });
        });
        return [...errores];
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

    f.addEventListener("input", () => { if (!aviso.hidden) mostrar(revisar()); });
});
