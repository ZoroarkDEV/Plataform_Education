---
title: "Listas e faixas com IN e BETWEEN"
module: mais-consultas-com-sql
order: 2
estimated_minutes: 20
language: sql
difficulty: beginner
objectives:
  - "Aceitar qualquer valor de uma lista com IN"
  - "Filtrar uma faixa de valores com BETWEEN, que inclui as duas pontas"
  - "Juntar os dois com AND"
completion_summary: "Filtrar por uma lista de opções com IN e por uma faixa de valores, com as pontas incluídas, com BETWEEN."
---

# Listas e faixas com IN e BETWEEN

## O que você vai aprender

- Aceitar qualquer valor de uma lista com `IN`.
- Filtrar uma faixa de valores com `BETWEEN`, que inclui as duas pontas.
- Juntar os dois com `AND`.

## IN: "é um destes?"

Para aceitar livros de Mistério **ou** de Contos, você já sabe escrever:
`WHERE genero = 'Mistério' OR genero = 'Contos'`. Funciona, mas com 5 gêneros fica enorme.

O `IN` faz a mesma coisa de um jeito curto. *In* quer dizer "dentro de" em inglês. Leia "o gênero está
**dentro desta lista**?". A lista vai entre **parênteses**, com os itens separados por **vírgula**, e cada texto com
suas próprias **aspas simples**.

```sql
-- Livros de Mistério ou de Contos
SELECT titulo, genero FROM livros WHERE genero IN ('Mistério', 'Contos');
```

| titulo | genero |
|---|---|
| Assassinato no Expresso do Oriente | Mistério |
| E Não Sobrou Nenhum | Mistério |
| Laços de Família | Contos |

## BETWEEN: "de um valor até outro"

*Between* quer dizer "entre". `BETWEEN 1930 AND 1940` quer dizer "de 1930 até 1940, **incluindo** 1930 e 1940".
O menor valor vem primeiro, e o `AND` aqui liga as duas pontas da faixa.

```sql
-- Livros publicados de 1930 a 1940
SELECT titulo, ano_publicacao FROM livros WHERE ano_publicacao BETWEEN 1930 AND 1940;
```

| titulo | ano_publicacao |
|---|---|
| O Hobbit | 1937 |
| Assassinato no Expresso do Oriente | 1934 |
| E Não Sobrou Nenhum | 1939 |

`BETWEEN 1930 AND 1940` é o mesmo que `ano_publicacao >= 1930 AND ano_publicacao <= 1940`. Com `>` e `<`
(sem o `=`), as pontas ficariam de fora.

## Agora é sua vez

Uma cliente quer um **Romance** ou um livro de **Contos** que custe **de R$ 29,90 a R$ 39,90**,
incluindo esses dois valores. Mostre o **título** e o **preço**. Lembre: no SQL o decimal usa ponto (29.90).

## Resumo

- `coluna IN ('a', 'b')` aceita qualquer valor da lista. É um jeito curto de escrever vários `OR`.
- `coluna BETWEEN menor AND maior` aceita a faixa **com** as duas pontas.
- Dá para juntar `IN` e `BETWEEN` no mesmo `WHERE` com `AND`.
