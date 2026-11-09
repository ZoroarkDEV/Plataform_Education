// Regra do "Seguir sem pontos" na tela de exercício (#29), alinhada à do servidor
// (LessonExerciseProgressDto.CanSkip): sem pass, SEM esgotar MaxAttempts e com
// FailuresBeforeSkip falhas. Arquivo separado e sem DOM para poder ser testado no Node.
(function (root) {
  "use strict";

  // Chamado depois de um envio errado (fail, error ou timeout).
  // untilRaw: data-failures-until-skip antes do envio ("" = sem progresso, não anuncia).
  // remainingAttempts: o que o /executar devolveu (null = sem limite; 0 = esgotou agora).
  // Devolve o novo valor do atributo e se o pulo deve ser anunciado.
  const afterFailure = (untilRaw, remainingAttempts) => {
    const raw = untilRaw == null ? "" : String(untilRaw).trim();
    const until = raw === "" ? NaN : Number(raw);
    if (!Number.isInteger(until) || until <= 0) {
      return { until: raw, announce: false };
    }

    const next = until - 1;
    // Esgotar as tentativas na mesma falha que alcançaria o limite: o servidor conclui com
    // AttemptsExhausted e não permite pular, então a tela também não anuncia.
    const exhausted = remainingAttempts === 0;
    return { until: String(next), announce: next === 0 && !exhausted };
  };

  const api = { afterFailure };
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.DevPathSkipRule = api;
  }
})(typeof window !== "undefined" ? window : globalThis);
