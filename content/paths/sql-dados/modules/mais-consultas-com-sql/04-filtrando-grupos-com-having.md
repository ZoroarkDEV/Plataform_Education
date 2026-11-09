---
title: "Filtrando grupos com HAVING"
module: mais-consultas-com-sql
order: 4
estimated_minutes: 25
language: sql
difficulty: beginner
objectives:
  - "Filtrar grupos com HAVING, depois do GROUP BY"
  - "Entender a diferença entre WHERE (filtra livros) e HAVING (filtra grupos)"
completion_summary: "Filtrar grupos pelo resumo com HAVING e saber quando usar WHERE ou HAVING."
---

# Filtrando grupos com HAVING

## O que você vai aprender

- Filtrar **grupos** com `HAVING`, depois do `GROUP BY`.
- Entender a diferença entre `WHERE` (filtra livros) e `HAVING` (filtra grupos).

## Relembrando o GROUP BY

Na última aula do módulo 1, o `GROUP BY` separou os livros em **pilhas**, uma por gênero, e o `SUM`/`COUNT`
resumiu cada pilha. Agora a pergunta é: "quais gêneros têm **mais de 10 unidades** em estoque?".
O filtro não é sobre um livro. É sobre a **pilha inteira**.

## HAVING: o WHERE dos grupos

*Having* quer dizer "tendo" em inglês. Leia "agrupe por gênero, **ficando só com os grupos que têm** mais de 10 unidades".
O `HAVING` vem **depois do GROUP BY** e pode usar as funções de resumo (`COUNT`, `SUM`, `AVG`).

```sql
-- Gêneros com mais de 10 unidades em estoque
SELECT genero, SUM(estoque) AS total_em_estoque
FROM livros
GROUP BY genero
HAVING SUM(estoque) > 10;
```

| genero | total_em_estoque |
|---|---|
| Romance | 21 |
| Fantasia | 13 |

Contos (9) e Mistério (7) ficaram de fora.

## WHERE ou HAVING?

- `WHERE` olha **cada livro**, **antes** de agrupar. Ele ainda não sabe quantos livros cada gênero tem.
- `HAVING` olha **cada grupo**, **depois** de agrupar. Ele já sabe a contagem e a soma.

Dá para usar os dois na mesma consulta:

```sql
-- Só livros abaixo de R$ 50; depois, só os gêneros com 2 ou mais desses livros
SELECT genero, COUNT(*) AS total_livros
FROM livros
WHERE preco < 50
GROUP BY genero
HAVING COUNT(*) >= 2;
```

| genero | total_livros |
|---|---|
| Romance | 3 |
| Mistério | 2 |

A ordem das partes é sempre: `SELECT` → `FROM` → `WHERE` → `GROUP BY` → `HAVING` → `ORDER BY` → `LIMIT`.

## Agora é sua vez

Quais gêneros têm **3 livros ou mais**? Mostre o **gênero** e a **quantidade de livros**.

## Resumo

- `HAVING` filtra grupos e vem depois do `GROUP BY`.
- `WHERE` filtra linhas antes de agrupar; `HAVING` filtra os grupos já resumidos.
- No `HAVING`, escreva a conta de novo (`COUNT(*) >= 2`), não o apelido.
