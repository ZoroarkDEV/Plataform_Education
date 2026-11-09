---
title: "Quem ficou sem par: LEFT JOIN"
module: mais-consultas-com-sql
order: 5
estimated_minutes: 25
language: sql
difficulty: beginner
objectives:
  - "Juntar tabelas sem perder linhas com LEFT JOIN"
  - "Reconhecer o vazio (null) que aparece quando não há par"
  - "Encontrar quem ficou sem par com IS NULL"
completion_summary: "Juntar tabelas sem perder quem não tem par, com LEFT JOIN, e achar quem ficou de fora com IS NULL."
---

# Quem ficou sem par: LEFT JOIN

## O que você vai aprender

- Juntar tabelas sem perder linhas com `LEFT JOIN`.
- Reconhecer o vazio (`null`) que aparece quando não há par.
- Encontrar quem ficou sem par com `IS NULL`.

## Relembrando: o INNER JOIN some com quem não tem par

No módulo 1, o `INNER JOIN` juntou livros e autores. Ele mostra só quem **tem par nos dois lados**. A autora
Conceição Evaristo (id 6) não tem nenhum livro na loja, por isso ela **some** do resultado:

```sql
SELECT COUNT(*) FROM autores INNER JOIN livros ON livros.autor_id = autores.id;
-- → 10 (uma linha por livro; Conceição Evaristo não aparece)
```

## LEFT JOIN: todos da primeira tabela ficam

*Left* quer dizer "esquerda". A tabela da esquerda é a que vem **no FROM**. O `LEFT JOIN` mantém **todas** as linhas
dela, tendo par ou não. Quando um autor não tem livro, as colunas do livro vêm **vazias**. O banco chama esse vazio
de **null** ("nulo", "sem valor"). Na tela ele aparece como *null*.

```sql
-- Autores do Brasil e seus livros, sem perder quem não tem livro
SELECT autores.nome, livros.titulo
FROM autores
LEFT JOIN livros ON livros.autor_id = autores.id
WHERE autores.pais = 'Brasil';
```

| nome | titulo |
|---|---|
| Machado de Assis | Dom Casmurro |
| Machado de Assis | Memórias Póstumas de Brás Cubas |
| Clarice Lispector | A Hora da Estrela |
| Clarice Lispector | Laços de Família |
| Conceição Evaristo | *null* |

Conceição Evaristo apareceu, com o título vazio. Com `INNER JOIN`, ela teria sumido.

## Como achar quem está vazio: IS NULL

O vazio não é um valor, então `= NULL` **nunca** dá certo. Para perguntar "está vazio?", o SQL usa
`IS NULL` ("é nulo"). O contrário é `IS NOT NULL`.

## Agora é sua vez

A livraria quer encomendar livros de quem ainda não tem nada na loja. Mostre o **nome** dos
**autores que não têm nenhum livro** cadastrado.

Siga os passos:

1. Comece pela tabela que não pode perder ninguém: `autores`.
2. Junte com `livros` usando `LEFT JOIN`, com a mesma regra do `ON` do módulo 1.
3. Fique só com as linhas em que o lado do livro está vazio.

## Quer ir além?

Este desafio é opcional e não conta para concluir a aula. Quantos livros cada autor tem, **incluindo quem tem
zero**? Use o `LEFT JOIN` com o `GROUP BY` do módulo 1. O resultado esperado tem 6 linhas:

| nome | total_livros |
|---|---|
| Clarice Lispector | 2 |
| Conceição Evaristo | 0 |
| Gabriel García Márquez | 1 |
| Agatha Christie | 2 |
| J.R.R. Tolkien | 3 |
| Machado de Assis | 2 |

Cuidado: com `COUNT(*)`, Conceição Evaristo aparece com 1, porque a linha vazia também é contada. Que coluna você
pode contar para que o vazio não entre na conta?

## Resumo

- `LEFT JOIN` mantém todas as linhas da tabela do `FROM`, mesmo sem par.
- Sem par, as colunas da outra tabela vêm vazias (`null`).
- Para testar o vazio, use `IS NULL`, nunca `= NULL`.
