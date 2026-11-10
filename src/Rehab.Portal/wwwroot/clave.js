// Ojo: muestra u oculta la contraseña del campo anterior.
document.querySelectorAll(".ojo").forEach(b => b.addEventListener("click", () => {
    const campo = b.previousElementSibling;
    const ver = campo.type === "password";
    campo.type = ver ? "text" : "password";
    b.setAttribute("aria-pressed", ver);
    b.setAttribute("aria-label", ver ? "Ocultar contraseña" : "Mostrar contraseña");
}));
