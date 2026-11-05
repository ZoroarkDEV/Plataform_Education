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

// Conclusão de aula (#27) + Executar (#30 B2).
(() => {
  const TEXTS = {
    emptyCode: "Escreva sua consulta antes de executar.",
    tooLarge: "Sua consulta está grande demais para executar aqui.",
    rateLimited: "Calma, muitas execuções seguidas. Espere alguns segundos e tente de novo. Isso não contou como tentativa.",
    platform: "O exercício está com um problema do nosso lado. Tente de novo em instantes. Isso não contou como tentativa.",
    notFound: "Não encontramos este exercício. Ele pode ter sido despublicado. Volte ao dashboard e escolha outra aula.",
    unauthorized: "Sua sessão expirou. Entre de novo para continuar.",
    saveError: "Não conseguimos salvar seu progresso. Confira sua conexão e tente de novo.",
    skipAnnounce: "Se preferir, você já pode seguir sem pontos.",
    lastAttempt: "Esta é sua última tentativa.",
    remaining: (n) => `Tentativas restantes: ${n}`,
    execute: "Executar",
    executing: "Executando…",
  };

  const MAX_CODE = 20000;

  const announce = (message) => {
    const region = document.getElementById("lesson-completion-live")
      || document.getElementById("copy-status");
    if (!region) return;
    region.textContent = "";
    setTimeout(() => { region.textContent = message; }, 50);
  };

  const focusAfterComplete = (root) => {
    const next = document.getElementById("next-lesson-link");
    if (next) {
      next.focus();
      return;
    }
    const msg = (root || document).querySelector("[data-completion-message]");
    if (msg) {
      if (!msg.hasAttribute("tabindex")) msg.setAttribute("tabindex", "-1");
      msg.focus();
    }
  };

  // Após HTMX trocar o bloco de conclusão (clique no botão / skip), move o foco.
  document.body.addEventListener("htmx:afterSwap", (event) => {
    const target = event.detail?.target;
    if (!target || target.id !== "lesson-completion") return;
    if (target.dataset.justCompleted === "true") {
      focusAfterComplete(document);
      const msg = target.querySelector("[data-completion-message]");
      if (msg) announce(msg.textContent.trim());
    }
  });

  document.body.addEventListener("htmx:responseError", (event) => {
    const target = event.detail?.target;
    if (!target || target.id !== "lesson-completion") return;
    let alert = target.querySelector("[data-completion-error]");
    if (!alert) {
      alert = document.createElement("p");
      alert.setAttribute("role", "alert");
      alert.dataset.completionError = "";
      alert.className = "mt-3 text-sm font-medium text-red-800 dark:text-red-200";
      target.appendChild(alert);
    }
    alert.textContent = TEXTS.saveError;
  });

  const getAntiforgery = async () => {
    const res = await fetch("/api/antiforgery/token", { credentials: "same-origin" });
    if (res.status === 401) throw Object.assign(new Error("401"), { status: 401 });
    if (!res.ok) throw new Error("token");
    return res.json();
  };

  const renderResults = (container, outcome) => {
    container.hidden = false;
    container.classList.remove("hidden");
    if (!outcome.columns?.length) {
      container.innerHTML = "";
      return;
    }
    const trunc = outcome.truncated
      ? `<p class="mt-2 text-sm text-slate-600 dark:text-slate-400">Mostrando as primeiras ${outcome.rows.length} linhas.</p>`
      : "";
    const head = outcome.columns.map((c) => `<th class="border border-slate-300 px-2 py-1 text-left dark:border-slate-600">${escapeHtml(c)}</th>`).join("");
    const body = (outcome.rows || []).map((row) =>
      `<tr>${row.map((cell) => `<td class="border border-slate-300 px-2 py-1 font-mono text-sm dark:border-slate-600">${cell == null ? "<em>null</em>" : escapeHtml(String(cell))}</td>`).join("")}</tr>`
    ).join("");
    container.innerHTML = `<div class="overflow-x-auto"><table class="min-w-full border-collapse text-sm" data-result-table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>${trunc}`;
  };

  const renderFeedback = (container, outcome, extras = "") => {
    const cases = (outcome.testCases || []).map((tc) =>
      `<li class="${tc.passed ? "text-emerald-800 dark:text-emerald-200" : "text-amber-800 dark:text-amber-200"}">${escapeHtml(tc.name)}: ${tc.passed ? "passou" : escapeHtml(tc.message || "falhou")}</li>`
    ).join("");
    const detail = outcome.technicalDetail
      ? `<details class="mt-2 text-sm"><summary class="cursor-pointer">Ver detalhe técnico</summary><pre class="mt-1 whitespace-pre-wrap font-mono text-xs">${escapeHtml(outcome.technicalDetail)}</pre></details>`
      : "";
    // #32: commonErrorMessage opcional; tolerante se ausente.
    const tip = outcome.commonErrorMessage
      ? `<p class="mt-2 rounded-md border border-slate-300 bg-slate-50 px-3 py-2 text-sm text-slate-800 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-200" data-common-error><span class="font-semibold">Dica:</span> ${escapeHtml(outcome.commonErrorMessage)}</p>`
      : "";
    container.innerHTML = `
      <p class="font-medium text-slate-900 dark:text-white" data-friendly-message>${escapeHtml(outcome.friendlyMessage || "")}</p>
      ${cases ? `<ul class="mt-2 list-disc space-y-1 pl-5 text-sm">${cases}</ul>` : ""}
      ${tip}
      ${detail}
      ${extras}`;
  };

  const escapeHtml = (s) => s.replace(/[&<>"']/g, (c) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[c]);

  const mapHttpError = async (res) => {
    if (res.status === 401) return { text: TEXTS.unauthorized };
    if (res.status === 404) return { text: TEXTS.notFound };
    if (res.status === 429) return { text: TEXTS.rateLimited };
    if (res.status === 503) return { text: TEXTS.platform };
    if (res.status === 400) {
      let code = "";
      try {
        const body = await res.clone().json();
        code = body.code || "";
      } catch { /* ignore */ }
      // ADR-007: code_empty | code_too_long | antiforgery_invalid | malformed_body.
      if (code === "code_empty") return { text: TEXTS.emptyCode };
      if (code === "code_too_long") return { text: TEXTS.tooLarge };
      if (code === "antiforgery_invalid") return { text: TEXTS.platform, retryAntiforgery: true };
      // malformed_body e qualquer code desconhecido: erro genérico (nunca a mensagem crua do servidor).
      return { text: TEXTS.platform };
    }
    return { text: TEXTS.platform };
  };

  const setupExecute = (root) => {
    root.querySelectorAll("[data-exercise]").forEach((section) => {
      if (section.dataset.executeReady) return;
      const exerciseId = section.dataset.exerciseId;
      if (!exerciseId) return;
      section.dataset.executeReady = "true";

      const button = section.querySelector("[data-execute-button]");
      const textarea = section.querySelector("[data-exercise-code]");
      const feedback = section.querySelector("[data-exercise-feedback]");
      const results = section.querySelector("[data-exercise-results]");
      const counter = section.querySelector("[data-attempts-counter]");
      if (!button || !textarea || !feedback) return;

      const setBusy = (busy) => {
        button.disabled = busy || button.getAttribute("aria-disabled") === "true";
        button.querySelector("[data-execute-idle]")?.classList.toggle("hidden", busy);
        button.querySelector("[data-execute-busy]")?.classList.toggle("hidden", !busy);
      };

      const updateAttempts = (remaining) => {
        if (!counter || remaining == null) return;
        section.dataset.remainingAttempts = String(remaining);
        counter.textContent = remaining === 1 ? TEXTS.lastAttempt : TEXTS.remaining(remaining);
      };

      const maybeRefreshCompletion = async (outcome) => {
        if (!outcome.lessonCompleted) return;
        const block = document.getElementById("lesson-completion");
        const url = block?.dataset.conclusaoUrl;
        if (!url || !window.htmx) return;
        // Não move o foco: conclusão veio do exercício.
        window.htmx.ajax("GET", url, { target: "#lesson-completion", swap: "outerHTML" });
      };

      button.addEventListener("click", async () => {
        if (button.getAttribute("aria-disabled") === "true") return;
        const code = textarea.value;
        if (!code || !code.trim()) {
          feedback.innerHTML = `<p role="alert" class="text-sm font-medium text-red-800 dark:text-red-200">${TEXTS.emptyCode}</p>`;
          return;
        }
        if (code.length > MAX_CODE) {
          feedback.innerHTML = `<p role="alert" class="text-sm font-medium text-red-800 dark:text-red-200">${TEXTS.tooLarge}</p>`;
          return;
        }

        setBusy(true);
        try {
          const postOnce = async (token, headerName) => fetch(`/api/exercicios/${exerciseId}/executar`, {
            method: "POST",
            credentials: "same-origin",
            headers: {
              "Content-Type": "application/json",
              [headerName || "RequestVerificationToken"]: token,
            },
            body: JSON.stringify({ code }),
          });

          let af = await getAntiforgery();
          let res = await postOnce(af.token, af.headerName);

          if (!res.ok) {
            const err = await mapHttpError(res);
            if (err.retryAntiforgery) {
              // antiforgery_invalid: busca token novo e tenta 1 vez em silêncio.
              af = await getAntiforgery();
              res = await postOnce(af.token, af.headerName);
              if (!res.ok) {
                const err2 = await mapHttpError(res);
                feedback.innerHTML = `<p role="alert" class="text-sm font-medium text-red-800 dark:text-red-200">${err2.text}</p>`;
                if (results) { results.innerHTML = ""; results.classList.add("hidden"); }
                return;
              }
            } else {
              feedback.innerHTML = `<p role="alert" class="text-sm font-medium text-red-800 dark:text-red-200">${err.text}</p>`;
              if (results) { results.innerHTML = ""; results.classList.add("hidden"); }
              return;
            }
          }

          const outcome = await res.json();
          if (outcome.remainingAttempts != null) updateAttempts(outcome.remainingAttempts);

          if (outcome.status === "attempts_exhausted") {
            button.setAttribute("aria-disabled", "true");
            button.disabled = true;
            let reason = section.querySelector("[data-executar-reason]");
            if (!reason) {
              reason = document.createElement("p");
              reason.dataset.executarReason = "";
              reason.id = "executar-disabled-reason";
              reason.className = "mt-2 text-sm text-slate-700 dark:text-slate-300";
              button.parentElement?.after(reason);
            }
            reason.textContent = "Você usou todas as tentativas deste exercício.";
            button.setAttribute("aria-describedby", reason.id);
          }

          let extras = "";
          const failed = Number(section.dataset.failedAttempts || "0");
          if (outcome.status === "fail" || outcome.status === "error" || outcome.status === "timeout") {
            const nextFailed = failed + 1;
            section.dataset.failedAttempts = String(nextFailed);
            if (nextFailed >= 5 && section.dataset.canSkip !== "true") {
              section.dataset.canSkip = "true";
              extras = `<p class="mt-2 text-sm text-slate-700 dark:text-slate-300">${TEXTS.skipAnnounce}</p>`;
              announce(TEXTS.skipAnnounce);
              // Pede o bloco de conclusão atualizado (Seguir sem pontos) sem mover foco.
              const block = document.getElementById("lesson-completion");
              const url = block?.dataset.conclusaoUrl;
              if (url && window.htmx) {
                window.htmx.ajax("GET", url, { target: "#lesson-completion", swap: "outerHTML" });
              }
            }
          }

          renderFeedback(feedback, outcome, extras);
          if (results) renderResults(results, outcome);
          await maybeRefreshCompletion(outcome);
        } catch (err) {
          const msg = err?.status === 401 ? TEXTS.unauthorized : TEXTS.platform;
          feedback.innerHTML = `<p role="alert" class="text-sm font-medium text-red-800 dark:text-red-200">${msg}</p>`;
        } finally {
          setBusy(false);
        }
      });
    });
  };

  // Salvando… no botão de concluir via HTMX.
  document.body.addEventListener("htmx:beforeRequest", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;
    if (!form.matches("[data-complete-form], [data-skip-form]")) return;
    const btn = form.querySelector("[data-complete-button], [data-skip-button]");
    if (!btn) return;
    btn.querySelector("[data-label-idle]")?.classList.add("hidden");
    btn.querySelector("[data-label-busy]")?.classList.remove("hidden");
  });

  document.addEventListener("DOMContentLoaded", () => setupExecute(document));
  document.addEventListener("htmx:load", (event) => setupExecute(event.target));
})();
