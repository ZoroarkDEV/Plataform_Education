---
title: "Buscando parte do texto com LIKE"
module: mais-consultas-com-sql
order: 1
estimated_minutes: 20
language: sql
difficulty: beginner
objectives:
  - "Buscar por um pedaço do texto com LIKE"
  - "Usar o % para dizer \"qualquer coisa aqui\""
  - "Lembrar que maiúsculas e minúsculas fazem diferença"
completion_summary: "Achar um livro lembrando só de um pedaço do nome, com LIKE e %."
---

# Buscando parte do texto com LIKE

## O que você vai aprender

- Buscar por um pedaço do texto com `LIKE`.
- Usar o `%` para dizer "qualquer coisa aqui".
- Lembrar que maiúsculas e minúsculas fazem diferença.

## Quando o sinal de igual não basta

No módulo 1 você filtrou com `=`: `WHERE genero = 'Romance'`. O `=` só encontra o texto **exatamente igual**.
Mas às vezes o cliente diz "não lembro o nome, só sei que tinha **Anel** no título".

Para isso existe o `LIKE`. *Like* quer dizer "parecido com" em inglês. Ele compara o texto com um **modelo**.
No modelo, o símbolo `%` quer dizer **"qualquer coisa aqui"**: nenhuma letra, uma letra ou muitas.

| modelo | quer dizer | exemplo que combina |
|---|---|---|
| `'C%'` | começa com C | Clarice Lispector |
| `'%a'` | termina com a | A Hora da Estrela |
| `'%Anel%'` | tem "Anel" em qualquer parte | O Senhor dos Anéis: A Sociedade do Anel |

O modelo é um texto, então vai entre **aspas simples**, como no `WHERE` do módulo 1.

## Exemplos

```sql
-- Autores cujo nome começa com C
SELECT nome FROM autores WHERE nome LIKE 'C%';
```

| nome |
|---|
| Clarice Lispector |
| Conceição Evaristo |

```sql
-- Livros com a palavra Anel em qualquer parte do título
SELECT titulo FROM livros WHERE titulo LIKE '%Anel%';
```

| titulo |
|---|
| O Senhor dos Anéis: A Sociedade do Anel |

**Cuidado com maiúsculas:** `'c%'` (c minúsculo) não encontra ninguém, porque os nomes começam com C maiúsculo.
Para o `LIKE`, "C" e "c" são letras diferentes.

## Agora é sua vez

Um cliente lembra só que o título **começa com a letra O**. Mostre o **título** e o **preço**.

## Resumo

- `LIKE` busca por um modelo de texto; `=` busca o texto exato.
- `%` quer dizer "qualquer coisa aqui": `'C%'` começa com C, `'%a'` termina com a, `'%Anel%'` contém Anel.
- No `LIKE`, maiúsculas e minúsculas contam.
