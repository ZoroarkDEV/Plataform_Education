---
title: "Resumindo dados com COUNT, SUM, AVG e GROUP BY"
module: primeiros-passos-com-sql
order: 5
estimated_minutes: 25
language: sql
difficulty: beginner
objectives:
  - "Resumir uma tabela com COUNT, SUM e AVG"
  - "Calcular um resumo por grupo com GROUP BY"
  - "Aplicar a regra: toda coluna do SELECT que não é resumo precisa estar no GROUP BY"
---

# Resumindo dados com COUNT, SUM, AVG e GROUP BY

## O que você vai aprender

- Resumir uma tabela com `COUNT`, `SUM` e `AVG`.
- Calcular um resumo por grupo com `GROUP BY`.
- Aplicar a regra: toda coluna do `SELECT` que não é resumo precisa estar no `GROUP BY`.

## Às vezes você não quer as linhas, e sim um resumo

Perguntas como "quantos livros temos?" ou "quantas unidades há no estoque?" não pedem uma lista:
pedem **um número**. Você até poderia trazer todas as linhas e contar à mão, mas o banco faz isso
direto e devolve só o resultado. Com 10 livros tanto faz; com 10 milhões de pedidos, faz toda a
diferença.

Para isso existem as **funções de agregação** (de resumo):

| Função | O que faz |
|---|---|
| `COUNT(*)` | conta as linhas |
| `SUM(coluna)` | soma os valores |
| `AVG(coluna)` | tira a média |
| `ROUND(valor, 2)` | arredonda para 2 casas decimais (útil com médias) |

## Exemplos: resumo da tabela inteira

```sql
-- Quantos livros a livraria tem?
SELECT COUNT(*) FROM livros;
-- → 10

-- Quantas unidades há no estoque, somando todos os livros?
SELECT SUM(estoque) AS total_em_estoque FROM livros;
-- → 50

-- Qual é o preço médio, arredondado para 2 casas?
SELECT ROUND(AVG(preco), 2) AS preco_medio FROM livros;
-- → 43.57
```

O `AS total_em_estoque` dá um **nome** à coluna do resultado. Sem ele, o banco inventa um nome
(como `sum`), que não diz muito a quem lê.

## Resumo por grupo: GROUP BY

E se a pergunta for "quantas unidades há em estoque **de cada gênero**?". Agora queremos um
resumo **por grupo**. O `GROUP BY` separa as linhas em grupos (um por gênero) e calcula o resumo
dentro de cada um:

```sql
-- Unidades em estoque por gênero
SELECT genero, SUM(estoque) AS total_em_estoque
FROM livros
GROUP BY genero
ORDER BY genero;
```

| genero | total_em_estoque |
|---|---|
| Contos | 9 |
| Fantasia | 13 |
| Mistério | 7 |
| Romance | 21 |

### A regra de ouro do GROUP BY

**Toda coluna do `SELECT` que não é um resumo precisa estar no `GROUP BY`.**

Por quê? Depois de agrupar, cada linha do resultado representa **um gênero inteiro**. Se você
pedir `titulo`, o banco não tem como saber qual dos títulos daquele gênero mostrar, e por isso
devolve um erro em vez de escolher um ao acaso.

## Agora é sua vez

Quantos títulos a livraria tem **em cada gênero**? No exercício abaixo, mostre o **gênero** e a
**quantidade de livros**. Dar um nome à coluna da contagem (por exemplo `total_livros`) é
recomendado, mas não é obrigatório.

## Resumo

- `COUNT(*)`, `SUM(coluna)` e `AVG(coluna)` resumem várias linhas em um valor.
- `GROUP BY coluna` calcula o resumo para cada grupo.
- Toda coluna do `SELECT` que não é resumo precisa estar no `GROUP BY`.
- `AS nome` dá um nome claro à coluna do resultado.
