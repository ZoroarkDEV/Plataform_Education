---
title: "Seu primeiro SELECT"
module: primeiros-passos-com-sql
order: 1
estimated_minutes: 15
language: sql
difficulty: beginner
objectives:
  - "Entender que um banco de dados guarda informações em tabelas, com linhas e colunas"
  - "Ler dados de uma tabela com SELECT ... FROM ..."
  - "Escolher quais colunas aparecem no resultado"
---

# Seu primeiro SELECT

## O que você vai aprender

- Entender que um banco de dados guarda informações em **tabelas**, com linhas e colunas.
- Ler dados de uma tabela com `SELECT ... FROM ...`.
- Escolher quais colunas aparecem no resultado.

## Tabelas: o banco é um conjunto de planilhas

Você não precisa instalar nada: as consultas desta aula rodam aqui no navegador.

Um banco de dados guarda informações em **tabelas**, parecidas com planilhas:

- cada **linha** é um registro (um livro, um autor);
- cada **coluna** é uma característica desse registro (título, preço, país...).

Nas próximas aulas vamos usar o banco de uma pequena **livraria**, com duas tabelas:

**`autores`**: quem escreveu os livros.

| id | nome | pais |
|---|---|---|
| 1 | Machado de Assis | Brasil |
| 2 | Clarice Lispector | Brasil |
| 3 | J.R.R. Tolkien | Reino Unido |
| 4 | Agatha Christie | Reino Unido |
| 5 | Gabriel García Márquez | Colômbia |
| 6 | Conceição Evaristo | Brasil |

**`livros`**: o que a livraria vende.

| id | titulo | autor_id | genero | ano_publicacao | preco | estoque |
|---|---|---|---|---|---|---|
| 1 | Dom Casmurro | 1 | Romance | 1899 | 29.90 | 12 |
| 2 | Memórias Póstumas de Brás Cubas | 1 | Romance | 1881 | 34.90 | 5 |
| 3 | A Hora da Estrela | 2 | Romance | 1977 | 39.90 | 0 |
| 4 | O Hobbit | 3 | Fantasia | 1937 | 49.90 | 8 |
| 5 | O Senhor dos Anéis: A Sociedade do Anel | 3 | Fantasia | 1954 | 69.90 | 3 |
| 6 | Assassinato no Expresso do Oriente | 4 | Mistério | 1934 | 32.50 | 7 |
| 7 | E Não Sobrou Nenhum | 4 | Mistério | 1939 | 36.00 | 0 |
| 8 | Cem Anos de Solidão | 5 | Romance | 1967 | 54.90 | 4 |
| 9 | O Silmarillion | 3 | Fantasia | 1977 | 59.90 | 2 |
| 10 | Laços de Família | 2 | Contos | 1960 | 27.90 | 9 |

## Pedindo dados ao banco: SELECT ... FROM ...

**SQL** é a linguagem usada para conversar com bancos de dados. Cada pedido que você escreve em SQL
se chama **consulta**.

Para **ler** dados, você faz um pedido ao banco: *"selecione estas colunas da tabela tal"*.
Em SQL, esse pedido se escreve assim:

```sql
-- Formato geral (modelo, não é uma consulta para rodar)
SELECT colunas FROM tabela;
```

- `SELECT` diz **quais colunas** você quer ver.
- `FROM` diz **de qual tabela** elas vêm.
- O `*` é um atalho para "todas as colunas".

Por que aprender isso primeiro? Porque quase tudo o que um sistema faz com um banco começa com
uma leitura: mostrar a lista de produtos, o perfil de um usuário, o extrato de uma conta.

## Exemplos

```sql
-- Mostra todas as colunas de todos os autores
SELECT * FROM autores;
```

Resultado: as 6 linhas da tabela `autores`, com as 3 colunas (`id`, `nome`, `pais`).

```sql
-- Mostra só o nome e o país de cada autor (colunas separadas por vírgula)
SELECT nome, pais FROM autores;
```

| nome | pais |
|---|---|
| Machado de Assis | Brasil |
| Clarice Lispector | Brasil |
| J.R.R. Tolkien | Reino Unido |
| Agatha Christie | Reino Unido |
| Gabriel García Márquez | Colômbia |
| Conceição Evaristo | Brasil |

Repare em quatro detalhes:

- As colunas aparecem **na ordem em que você pediu**.
- As colunas são separadas por **vírgula**, e a última não leva vírgula.
- O `;` marca o fim da consulta.
- Tanto faz escrever `SELECT` ou `select`. Usamos maiúsculas porque deixam a consulta mais fácil de
  ler.

### Por que não usar sempre `SELECT *`?

O `*` é ótimo para dar uma olhada rápida numa tabela. Num sistema de verdade, é melhor pedir só as
colunas de que você precisa: a consulta fica mais clara e o banco não envia dados que ninguém vai
usar.

## Agora é sua vez

A livraria quer uma **lista de preços**. No exercício abaixo, mostre o **título** e o **preço** de
**todos os livros**. Se travar, use as dicas: elas não tiram pontos.

## Resumo

- Tabelas guardam dados em linhas (registros) e colunas (características).
- `SELECT colunas FROM tabela;` lê dados de uma tabela.
- `*` traz todas as colunas; para escolher, liste os nomes separados por vírgula.
