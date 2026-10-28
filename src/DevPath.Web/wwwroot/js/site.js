// Menu mobile do header: alterna a visibilidade do menu e o aria-expanded do botão.
document.addEventListener("DOMContentLoaded", () => {
  const button = document.querySelector("[data-menu-toggle]");
  const menu = document.getElementById("menu-mobile");
  if (!button || !menu) return;

  button.addEventListener("click", () => {
    const isOpen = button.getAttribute("aria-expanded") === "true";
    button.setAttribute("aria-expanded", String(!isOpen));
    menu.classList.toggle("hidden", isOpen);
  });
});
