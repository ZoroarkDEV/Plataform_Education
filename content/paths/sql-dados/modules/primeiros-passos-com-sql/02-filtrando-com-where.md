---
title: "Filtrando com WHERE"
module: primeiros-passos-com-sql
order: 2
estimated_minutes: 20
language: sql
difficulty: beginner
objectives:
  - "Filtrar linhas com WHERE usando =, < e >"
  - "Escrever textos entre aspas simples e números sem aspas"
  - "Combinar condições com AND e entender a diferença para OR"
---

# Filtrando com WHERE

## O que você vai aprender

- Filtrar linhas com `WHERE` usando `=`, `<` e `>`.
- Escrever textos entre aspas simples e números sem aspas.
- Combinar condições com `AND` e entender a diferença para `OR`.

## SELECT escolhe colunas, WHERE escolhe linhas

Na aula anterior você escolheu **quais colunas** ver. Quase sempre, porém, você não quer **todas
as linhas**: quer só os livros esgotados, só os clientes de uma cidade, só os pedidos de hoje.

É para isso que serve o `WHERE`. Ele funciona como o filtro de uma planilha: o banco olha linha
por linha e só devolve as que atendem à condição.

Uma **condição** é uma pergunta de sim ou não feita para cada linha, por exemplo "o estoque é
zero?". Só as linhas com resposta "sim" aparecem no resultado.

```sql
-- Formato geral (modelo, não é uma consulta para rodar)
SELECT colunas FROM tabela WHERE condição;
```

Por que filtrar no banco? Porque ele foi feito para isso: devolve só as linhas que você pediu, em
vez de mandar a tabela inteira para alguém filtrar.

## Comparando valores

| Operador | Significa | Exemplo |
|---|---|---|
| `=` | igual a | `estoque = 0` |
| `<` | menor que | `preco < 60` |
| `>` | maior que | `ano_publicacao > 1950` |
| `<=` / `>=` | menor/maior ou igual | `preco <= 30` |
| `<>` | diferente de | `genero <> 'Romance'` |

Duas regras que evitam a maioria dos erros:

- **Texto** vai entre **aspas simples**: `'Romance'`. Aspas duplas, em SQL, são para nomes de
  colunas, não para textos.
- **Números** vão **sem aspas**, com **ponto** decimal: `60` ou `59.90` (nada de `R$` ou vírgula).

E um detalhe importante: em textos, **maiúsculas e minúsculas fazem diferença**. `'Fantasia'` e
`'fantasia'` são valores diferentes para o banco.

## Exemplos

```sql
-- Livros esgotados (estoque igual a zero)
SELECT titulo, estoque FROM livros WHERE estoque = 0;
```

| titulo | estoque |
|---|---|
| A Hora da Estrela | 0 |
| E Não Sobrou Nenhum | 0 |

```sql
-- Livros publicados antes de 1900
SELECT titulo, ano_publicacao FROM livros WHERE ano_publicacao < 1900;
```

| titulo | ano_publicacao |
|---|---|
| Dom Casmurro | 1899 |
| Memórias Póstumas de Brás Cubas | 1881 |

## Duas condições: AND e OR

- `AND`: as **duas** condições precisam ser verdadeiras.
- `OR`: basta **uma** delas ser verdadeira.

```sql
-- Romances que ainda têm estoque (as duas coisas ao mesmo tempo)
SELECT titulo, estoque FROM livros WHERE genero = 'Romance' AND estoque > 0;
```

| titulo | estoque |
|---|---|
| Dom Casmurro | 12 |
| Memórias Póstumas de Brás Cubas | 5 |
| Cem Anos de Solidão | 4 |

```sql
-- Livros de Mistério ou de Contos (qualquer um dos dois gêneros)
SELECT titulo, genero FROM livros WHERE genero = 'Mistério' OR genero = 'Contos';
```

| titulo | genero |
|---|---|
| Assassinato no Expresso do Oriente | Mistério |
| E Não Sobrou Nenhum | Mistério |
| Laços de Família | Contos |

Uma forma de lembrar: `AND` **estreita** o resultado (menos linhas), `OR` **amplia** (mais linhas).

## Agora é sua vez

Um cliente procura livros de **Fantasia** que custem **menos de R$ 60**. No exercício abaixo,
mostre o **título** e o **preço** desses livros.

## Resumo

- `WHERE condição` filtra as linhas; `SELECT` continua escolhendo as colunas.
- Texto entre aspas simples (`'Fantasia'`), número sem aspas e com ponto (`59.90`).
- `AND` exige as duas condições; `OR` aceita qualquer uma.
