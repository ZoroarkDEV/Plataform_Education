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

// Tela de aula (#12): botão "Copiar" de cada bloco de código e alternância de tema.
// Sem jQuery. Os botões vêm com o atributo hidden e só aparecem se este script rodar.
(() => {
  const status = () => document.getElementById("copy-status");

  const announce = (message) => {
    const region = status();
    if (!region) return;
    region.textContent = "";
    // Pequeno atraso para o leitor de tela anunciar mesmo se a mensagem repetir.
    setTimeout(() => { region.textContent = message; }, 50);
  };

  const copyText = async (text) => {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return;
    }
    // Plano B (http sem TLS): textarea temporária + execCommand.
    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand("copy");
    area.remove();
    if (!ok) throw new Error("copy failed");
  };

  const setupCopyButtons = (root) => {
    root.querySelectorAll("[data-copy-code]").forEach((button) => {
      if (button.dataset.copyReady) return;
      button.dataset.copyReady = "true";
      button.hidden = false;

      button.addEventListener("click", async () => {
        const code = button.closest("[data-code-block]")?.querySelector("pre code");
        if (!code) return;
        try {
          await copyText(code.textContent ?? "");
          button.textContent = "Copiado!";
          announce("Código copiado. Agora é só colar onde quiser.");
        } catch {
          button.textContent = "Não copiou";
          announce("Não foi possível copiar. Selecione o código e use Ctrl+C.");
        }
        clearTimeout(button._resetTimer);
        button._resetTimer = setTimeout(() => { button.textContent = "Copiar"; }, 2000);
      });
    });
  };

  const setupThemeToggle = (root) => {
    root.querySelectorAll("[data-theme-toggle]").forEach((button) => {
      if (button.dataset.themeReady) return;
      button.dataset.themeReady = "true";
      button.hidden = false;

      const label = button.querySelector("[data-theme-state]");
      const render = () => {
        const dark = document.documentElement.classList.contains("dark");
        button.setAttribute("aria-pressed", String(dark));
        if (label) label.textContent = dark ? "ligado" : "desligado";
      };

      button.addEventListener("click", () => {
        const dark = !document.documentElement.classList.contains("dark");
        document.documentElement.classList.toggle("dark", dark);
        try { localStorage.setItem("devpath-theme", dark ? "dark" : "light"); } catch { /* ignora */ }
        render();
      });
      render();
    });
  };

  // Dicas do exercício: sem JS todas aparecem (fechadas). Com JS, só a primeira aparece; abrir
  // uma dica libera a seguinte, e o leitor de tela é avisado.
  const setupHints = (root) => {
    root.querySelectorAll("[data-hints]").forEach((list) => {
      if (list.dataset.hintsReady) return;
      list.dataset.hintsReady = "true";
      const items = [...list.querySelectorAll("[data-hint]")];
      items.forEach((item, index) => {
        if (index > 0) item.hidden = true;
        item.querySelector("details")?.addEventListener("toggle", (event) => {
          const next = items[index + 1];
          if (event.target.open && next && next.hidden) {
            next.hidden = false;
            announce(`Dica ${index + 2} de ${items.length} disponível.`);
          }
        });
      });
    });
  };

  const setup = (root) => {
    setupCopyButtons(root);
    setupThemeToggle(root);
    setupHints(root);
  };

  document.addEventListener("DOMContentLoaded", () => setup(document));
  // Conteúdo trazido pelo HTMX também ganha os botões.
  document.addEventListener("htmx:load", (event) => setup(event.target));
})();

// Tema em todo o app (#11, R7): o botão do header e o da aula (#12) ficam em sincronia.
// Roda depois do handler de cada botão (o clique sobe até o document).
document.addEventListener("click", (event) => {
  if (!event.target.closest?.("[data-theme-toggle]")) return;
  const dark = document.documentElement.classList.contains("dark");
  document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
    button.setAttribute("aria-pressed", String(dark));
    const label = button.querySelector("[data-theme-state]");
    if (label) label.textContent = dark ? "ligado" : "desligado";
  });
});
