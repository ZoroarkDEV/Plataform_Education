---
title: "Sem repetições com DISTINCT"
module: mais-consultas-com-sql
order: 3
estimated_minutes: 20
language: sql
difficulty: beginner
objectives:
  - "Tirar linhas repetidas do resultado com DISTINCT"
  - "Entender que o DISTINCT compara a linha inteira"
completion_summary: "Mostrar cada valor uma vez só, sem repetição, com DISTINCT."
---

# Sem repetições com DISTINCT

## O que você vai aprender

- Tirar linhas repetidas do resultado com `DISTINCT`.
- Entender que o `DISTINCT` compara a **linha inteira**.

## Por que aparece repetido?

Pergunta: "de quais países são os nossos autores?". Se você pedir só a coluna `pais`, vem uma linha **por autor**:

```sql
SELECT pais FROM autores;
```

| pais |
|---|
| Brasil |
| Brasil |
| Reino Unido |
| Reino Unido |
| Colômbia |
| Brasil |

São 6 autores, então vêm 6 linhas, e "Brasil" aparece 3 vezes. A pergunta era sobre **países**, não sobre autores.

## DISTINCT: cada linha uma vez só

*Distinct* quer dizer "diferente" ou "distinto". `SELECT DISTINCT` mostra cada linha diferente **uma vez só**.
Ele vem **logo depois do SELECT**, antes das colunas.

```sql
-- Países dos autores, sem repetir
SELECT DISTINCT pais FROM autores;
```

| pais |
|---|
| Reino Unido |
| Brasil |
| Colômbia |

## O DISTINCT olha a linha inteira

Com mais de uma coluna, duas linhas só são "repetidas" se **todas** as colunas forem iguais:

```sql
-- Cada combinação de gênero e autor, uma vez só
SELECT DISTINCT genero, autor_id FROM livros ORDER BY genero, autor_id;
```

| genero | autor_id |
|---|---|
| Contos | 2 |
| Fantasia | 3 |
| Mistério | 4 |
| Romance | 1 |
| Romance | 2 |
| Romance | 5 |

Romance aparece 3 vezes, porque são 3 autores diferentes. Se você quer cada **gênero** uma vez só, peça só o gênero.

## Agora é sua vez

A livraria vai fazer uma estante de livros **abaixo de R$ 40**. Em quais **gêneros** há livros
nessa faixa? Mostre **cada gênero uma vez só**.

## Resumo

- `SELECT DISTINCT colunas` tira as linhas repetidas do resultado.
- O `DISTINCT` vem logo depois do `SELECT`.
- Ele compara a linha inteira: mais colunas, menos repetições para tirar.
- Ele funciona junto com o `WHERE`: primeiro o filtro, depois tira as repetições.
