---
title: "Ordenando e limitando com ORDER BY e LIMIT"
module: primeiros-passos-com-sql
order: 3
estimated_minutes: 20
language: sql
difficulty: beginner
objectives:
  - "Ordenar o resultado com ORDER BY, do menor para o maior (ASC) e do maior para o menor (DESC)"
  - "Pegar só as primeiras linhas com LIMIT"
  - "Saber por que, sem ORDER BY, o banco não garante ordem nenhuma"
---

# Ordenando e limitando com ORDER BY e LIMIT

## O que você vai aprender

- Ordenar o resultado com `ORDER BY`, do menor para o maior (`ASC`) e do maior para o menor (`DESC`).
- Pegar só as primeiras linhas com `LIMIT`.
- Saber por que, sem `ORDER BY`, o banco não garante ordem nenhuma.

## Sem ORDER BY, não há ordem garantida

Até agora os resultados vieram numa ordem que *parece* a da tabela. Isso é coincidência: o banco
devolve as linhas na ordem que for mais rápida para ele, e essa ordem pode mudar (depois de uma
atualização, com mais dados, numa versão nova do banco).

Por isso a regra é simples: **se a ordem importa, peça a ordem**. Em SQL, isso é o `ORDER BY`.

```sql
-- Formato geral (modelo, não é uma consulta para rodar)
SELECT colunas FROM tabela ORDER BY coluna;
```

- `ORDER BY coluna` ordena do menor para o maior. É o mesmo que escrever `ASC` (*ascending*).
- `ORDER BY coluna DESC` ordena do maior para o menor (*descending*).
- Textos são ordenados em ordem alfabética; números, pelo valor.

## Exemplos

```sql
-- Livros do mais antigo para o mais novo
SELECT titulo, ano_publicacao FROM livros ORDER BY ano_publicacao;
```

| titulo | ano_publicacao |
|---|---|
| Memórias Póstumas de Brás Cubas | 1881 |
| Dom Casmurro | 1899 |
| Assassinato no Expresso do Oriente | 1934 |
| O Hobbit | 1937 |
| E Não Sobrou Nenhum | 1939 |
| O Senhor dos Anéis: A Sociedade do Anel | 1954 |
| Laços de Família | 1960 |
| Cem Anos de Solidão | 1967 |
| A Hora da Estrela | 1977 |
| O Silmarillion | 1977 |

**Empates:** "A Hora da Estrela" e "O Silmarillion" são de 1977. Quando dois valores empatam, a
ordem entre eles **pode variar**. Se isso importar, ordene por uma segunda coluna:
`ORDER BY ano_publicacao, titulo`.

## Pegando só as primeiras linhas: LIMIT

Muitas telas mostram só um pedaço do resultado: "os 5 mais vendidos", "os 10 mais recentes".
O `LIMIT` faz isso: ele corta o resultado **depois** de ordenar. Por isso vem **por último**.

```sql
-- Os 3 livros mais baratos
SELECT titulo, preco FROM livros ORDER BY preco ASC LIMIT 3;
```

| titulo | preco |
|---|---|
| Laços de Família | 27.90 |
| Dom Casmurro | 29.90 |
| Assassinato no Expresso do Oriente | 32.50 |

Por que primeiro ordenar e depois limitar? Porque "os 3 mais baratos" só faz sentido depois de
saber quem é mais barato. Um `LIMIT` sem `ORDER BY` devolve 3 linhas quaisquer.

A ordem das partes da consulta é sempre esta:

```sql
-- Formato geral (modelo, não é uma consulta para rodar)
SELECT colunas
FROM tabela
WHERE condição      -- opcional
ORDER BY coluna     -- opcional
LIMIT quantidade;   -- opcional, sempre por último
```

## Agora é sua vez

Monte a vitrine **"Premium"**: os **3 livros mais caros**, do **mais caro para o mais barato**,
com **título** e **preço**. Aqui a ordem das linhas conta.

## Resumo

- Sem `ORDER BY`, a ordem do resultado não é garantida.
- `ORDER BY coluna` (ou `ASC`) vai do menor para o maior; `DESC` vai do maior para o menor.
- `LIMIT n` pega as `n` primeiras linhas e vem no final, depois do `ORDER BY`.
