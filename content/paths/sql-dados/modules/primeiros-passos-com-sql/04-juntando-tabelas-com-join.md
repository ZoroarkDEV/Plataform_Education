---
title: "Juntando tabelas com JOIN"
module: primeiros-passos-com-sql
order: 4
estimated_minutes: 25
language: sql
difficulty: beginner
objectives:
  - "Entender por que os dados ficam em tabelas separadas, ligadas por um número"
  - "Combinar duas tabelas com INNER JOIN ... ON ..."
  - "Usar tabela.coluna e apelidos quando as duas tabelas têm uma coluna com o mesmo nome"
---

# Juntando tabelas com JOIN

## O que você vai aprender

- Entender por que os dados ficam em tabelas separadas, ligadas por um número.
- Combinar duas tabelas com `INNER JOIN ... ON ...`.
- Usar `tabela.coluna` e apelidos quando as duas tabelas têm uma coluna com o mesmo nome.

## Por que os dados ficam em tabelas separadas?

A livraria tem duas tabelas. Uma tabela é como uma planilha: tem **linhas** e **colunas**.

- A tabela `livros` tem uma linha para cada livro.
- A tabela `autores` tem uma linha para cada autor.

A tabela `livros` **não** guarda o nome do autor. Ela guarda só o **número** do autor, na coluna
`autor_id`. O nome fica na tabela `autores`, na coluna `nome`. Cada autor tem um número único, na
coluna `id`.

Veja como um livro aponta para o seu autor:

| livro (livros.titulo) | livros.autor_id | aponta para autores.id | autor (autores.nome) |
|---|---|---|---|
| O Hobbit | 3 | 3 | J.R.R. Tolkien |
| O Silmarillion | 3 | 3 | J.R.R. Tolkien |

Leia assim: "O Hobbit tem `autor_id` 3. Na tabela `autores`, o autor de `id` 3 é Tolkien".

Por que não escrever o nome do autor em cada livro? Tolkien tem 3 livros. Se o nome estivesse
nos 3, uma correção exigiria mudar 3 linhas. Se você esquecesse uma, a loja mostraria dois nomes
diferentes para a mesma pessoa. Com o nome guardado **uma vez só**, isso não acontece.

O preço disso: para saber "quem escreveu cada livro?", precisamos **juntar** as duas tabelas.
É isso que o `JOIN` faz. *Join* quer dizer "juntar" em inglês.

## INNER JOIN ... ON ...

Este é o formato geral. É só um modelo, não é para rodar:

```sql
-- Formato geral (modelo, não é uma consulta para rodar)
SELECT colunas
FROM tabela_a
INNER JOIN tabela_b ON tabela_a.coluna = tabela_b.coluna;
```

Cada parte tem uma função:

- `FROM livros`: a primeira tabela, como você já fazia.
- `INNER JOIN autores`: a segunda tabela, a que vamos juntar.
- `ON livros.autor_id = autores.id`: a regra para juntar. *On* quer dizer "com a condição".
  Aqui a regra é: "cole cada livro ao autor cujo `id` é igual ao `autor_id` do livro".

### O que é `tabela.coluna`?

As duas tabelas têm uma coluna chamada `id`. Se você escrever só `id`, o banco não sabe de
qual tabela é. Por isso escrevemos o nome da tabela, um ponto e o nome da coluna:

- `livros.id` é o número do livro.
- `autores.id` é o número do autor.

## Exemplos

```sql
-- Título e país do autor dos 3 primeiros livros
SELECT livros.titulo, autores.pais
FROM livros
INNER JOIN autores ON livros.autor_id = autores.id
WHERE livros.id <= 3;
```

### Lendo a consulta passo a passo

1. `FROM livros`: comece pela tabela de livros.
2. `INNER JOIN autores ON livros.autor_id = autores.id`: para cada livro, procure o autor com o
   mesmo número e cole os dados dele ao lado do livro.
3. `WHERE livros.id <= 3`: fique só com os livros de número 1, 2 e 3.
4. `SELECT livros.titulo, autores.pais`: mostre só o título do livro e o país do autor.

Resultado:

| titulo | pais |
|---|---|
| Dom Casmurro | Brasil |
| Memórias Póstumas de Brás Cubas | Brasil |
| A Hora da Estrela | Brasil |

Repare: o `WHERE`, que você já conhece, continua funcionando depois do `JOIN`.

### Apelidos deixam a consulta mais curta

Escrever `livros.` e `autores.` toda hora cansa. Você pode dar um **apelido** a cada tabela.
Um apelido é um nome curto, criado com `AS`:

- `FROM livros AS l` quer dizer "a partir de agora, `l` significa `livros`".
- `INNER JOIN autores AS a` quer dizer "`a` significa `autores`".

```sql
-- Os livros de Agatha Christie, com o nome da autora
SELECT l.titulo, a.nome
FROM livros AS l
INNER JOIN autores AS a ON l.autor_id = a.id
WHERE a.nome = 'Agatha Christie';
```

| titulo | nome |
|---|---|
| Assassinato no Expresso do Oriente | Agatha Christie |
| E Não Sobrou Nenhum | Agatha Christie |

Atenção: o apelido só existe dentro daquela consulta. Para usar `l.titulo`, você precisa ter
escrito `FROM livros AS l` na mesma consulta.

### INNER JOIN mostra só o que tem par

*Inner* quer dizer "de dentro". O `INNER JOIN` mostra só as linhas que encontraram par nas duas
tabelas.

Exemplo: a autora Conceição Evaristo está na tabela `autores`, com `id` 6. Mas nenhum livro tem
`autor_id = 6`. Ela não tem par. Por isso ela **não aparece** no resultado. Mais adiante na
trilha, você vai conhecer o `LEFT JOIN`, que mostra também quem ficou sem par.

## Agora é sua vez

Monte o **catálogo**: o **título** de cada livro e o **nome do autor**.

Siga os passos:

1. Comece pela tabela `livros`.
2. Junte com a tabela `autores`.
3. Escreva a regra do `ON`: qual coluna de `livros` guarda o número do autor?
4. No `SELECT`, peça o título do livro e o nome do autor.

**Desafio extra (opcional, não vale para concluir a aula):** mostre só os livros de autores do
**Brasil**. Devem vir 4 livros: Dom Casmurro, Memórias Póstumas de Brás Cubas, A Hora da Estrela
e Laços de Família.

## Resumo

- Dados relacionados ficam em tabelas separadas, ligadas por um número (`livros.autor_id` aponta para `autores.id`).
- `INNER JOIN tabela ON regra` junta as tabelas. O `ON` diz como as linhas se casam.
- `tabela.coluna` diz de qual tabela é a coluna. Apelidos (`livros AS l`) encurtam a escrita.
- `INNER JOIN` só mostra as linhas que têm par nas duas tabelas.
