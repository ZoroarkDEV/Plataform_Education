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

Algumas perguntas não pedem uma lista. Pedem **um número**. Por exemplo:

- "Quantos livros a livraria tem?"
- "Quantas unidades há no estoque?"

Você poderia trazer todas as linhas e contar à mão. Mas o banco faz a conta e devolve só o
resultado. Com 10 livros, tanto faz. Com 10 milhões de pedidos, faz toda a diferença.

## Funções de resumo

Para isso existem as **funções de agregação**. "Agregar" quer dizer juntar muitos valores em um
só. Pense nelas como **funções de resumo**.

Uma função tem um nome e parênteses. Dentro dos parênteses vai o que ela deve usar:

| Função | O que faz | Exemplo na livraria |
|---|---|---|
| `COUNT(*)` | conta as linhas | quantos livros existem |
| `SUM(coluna)` | soma os valores da coluna | total de unidades no estoque |
| `AVG(coluna)` | calcula a média da coluna | preço médio dos livros |

Em `COUNT(*)`, o `*` quer dizer "conte as linhas inteiras". Não importa qual coluna.

Mais uma função útil:

- `ROUND(valor, 2)` arredonda um número para 2 casas depois da vírgula. Ela não resume nada.
  Serve para deixar médias fáceis de ler.

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

Lendo a segunda consulta passo a passo:

1. `FROM livros`: use a tabela de livros.
2. `SUM(estoque)`: some a coluna `estoque` de todas as linhas.
3. `AS total_em_estoque`: dê o nome `total_em_estoque` à coluna do resultado.

Sem o `AS`, o banco inventa um nome, como `sum`. Esse nome não diz muito a quem lê. É o mesmo
`AS` da aula de JOIN. Lá ele dava apelido a uma tabela. Aqui ele dá nome a uma coluna.

## Resumo por grupo: GROUP BY

Agora a pergunta muda: "quantas unidades há em estoque **de cada gênero**?". Gênero é o tipo
do livro, como Romance ou Fantasia.

Não queremos um número só. Queremos **um número para cada gênero**. Para isso serve o
`GROUP BY`. *Group by* quer dizer "agrupar por".

```sql
-- Unidades em estoque por gênero
SELECT genero, SUM(estoque) AS total_em_estoque
FROM livros
GROUP BY genero
ORDER BY genero;
```

### Lendo a consulta passo a passo

1. `FROM livros`: use a tabela de livros.
2. `GROUP BY genero`: separe os livros em pilhas, uma pilha para cada gênero. Todos os
   romances numa pilha, todos os de fantasia em outra, e assim por diante.
3. `SELECT genero, SUM(estoque)`: para cada pilha, mostre o gênero e some o estoque daquela
   pilha.
4. `ORDER BY genero`: coloque as linhas do resultado em ordem alfabética de gênero.

Resultado: uma linha por gênero.

| genero | total_em_estoque |
|---|---|
| Contos | 9 |
| Fantasia | 13 |
| Mistério | 7 |
| Romance | 21 |

### A regra de ouro do GROUP BY

**Toda coluna do `SELECT` que não é um resumo precisa estar no `GROUP BY`.**

Por quê? Depois de agrupar, cada linha do resultado representa **uma pilha inteira**, ou seja,
um gênero inteiro. A pilha de Romance tem vários títulos. Se você pedir `titulo`, qual deles o
banco deveria mostrar? Ele não tem como saber. Por isso devolve um erro, em vez de escolher um
ao acaso.

- Pode: `SELECT genero, COUNT(*) ... GROUP BY genero`. O `genero` está no `GROUP BY`.
- Não pode: `SELECT titulo, COUNT(*) ... GROUP BY genero`. O `titulo` não é resumo e não está no
  `GROUP BY`.

## Agora é sua vez

Quantos títulos a livraria tem **em cada gênero**? No exercício abaixo, mostre o **gênero** e a
**quantidade de livros**.

Siga os passos:

1. Comece pela tabela `livros`.
2. Agrupe por gênero.
3. No `SELECT`, peça o gênero e a função que conta linhas.

Dar um nome à coluna da contagem (por exemplo `total_livros`) é recomendado, mas não é
obrigatório.

## Resumo

- `COUNT(*)`, `SUM(coluna)` e `AVG(coluna)` resumem várias linhas em um valor.
- `GROUP BY coluna` separa as linhas em grupos e calcula o resumo de cada grupo.
- Toda coluna do `SELECT` que não é resumo precisa estar no `GROUP BY`.
- `AS nome` dá um nome claro à coluna do resultado.
