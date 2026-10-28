---
title: "Juntando tabelas com JOIN"
module: primeiros-passos-com-sql
order: 4
estimated_minutes: 25
language: sql
difficulty: beginner
objectives:
  - "Entender por que os dados ficam separados em tabelas ligadas por um número (chave)"
  - "Combinar duas tabelas com INNER JOIN ... ON ..."
  - "Usar tabela.coluna e apelidos para evitar colunas ambíguas"
---

# Juntando tabelas com JOIN

## O que você vai aprender

- Entender por que os dados ficam separados em tabelas ligadas por um número (chave).
- Combinar duas tabelas com `INNER JOIN ... ON ...`.
- Usar `tabela.coluna` e apelidos para evitar colunas ambíguas.

## Por que os dados ficam em tabelas separadas?

Olhe de novo a tabela `livros`: ela **não** guarda o nome do autor, só o **número** dele, na
coluna `autor_id`. O nome está na tabela `autores`.

| livros.titulo | livros.autor_id | | autores.id | autores.nome |
|---|---|---|---|---|
| O Hobbit | 3 | → | 3 | J.R.R. Tolkien |
| O Silmarillion | 3 | → | 3 | J.R.R. Tolkien |

Por que não escrever o nome do autor em cada livro? Porque Tolkien tem 3 livros na loja. Se o
nome estivesse repetido em cada um, corrigir uma grafia exigiria mudar 3 linhas, e bastaria
esquecer uma para os dados ficarem inconsistentes. Guardando o autor **uma vez só** e apontando
para ele pelo número, cada informação mora em um único lugar.

O preço dessa organização é que, para responder "quem escreveu cada livro?", precisamos
**juntar** as duas tabelas. É isso que o `JOIN` faz.

## INNER JOIN ... ON ...

```sql
-- Formato geral (modelo, não é uma consulta para rodar)
SELECT colunas
FROM tabela_a
INNER JOIN tabela_b ON tabela_a.coluna = tabela_b.coluna;
```

- `INNER JOIN autores` diz **com qual tabela** juntar.
- `ON livros.autor_id = autores.id` diz **como as linhas se casam**: o `autor_id` do livro é igual
  ao `id` do autor.
- Como as duas tabelas têm uma coluna `id`, escrevemos `tabela.coluna` (`livros.id`, `autores.id`)
  para o banco saber de qual estamos falando.

## Exemplos

```sql
-- Título e país do autor dos 3 primeiros livros
SELECT livros.titulo, autores.pais
FROM livros
INNER JOIN autores ON livros.autor_id = autores.id
WHERE livros.id <= 3;
```

| titulo | pais |
|---|---|
| Dom Casmurro | Brasil |
| Memórias Póstumas de Brás Cubas | Brasil |
| A Hora da Estrela | Brasil |

Repare que o `WHERE`, que você já conhece, continua funcionando depois do `JOIN`.

### Apelidos deixam a consulta mais curta

Escrever `livros.` e `autores.` toda hora cansa. Você pode dar um **apelido** a cada tabela com
`AS` e usar o apelido no lugar do nome:

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

O apelido só existe dentro daquela consulta. Se você escrever `l.titulo`, precisa ter criado o
`l` no `FROM livros AS l`.

### INNER JOIN mostra só o que casa dos dois lados

A autora Conceição Evaristo está na tabela `autores`, mas ainda não tem livros cadastrados. Por
isso ela **não aparece** em nenhum resultado com `INNER JOIN`: não existe livro com
`autor_id = 6` para casar com ela. Mais adiante na trilha você vai conhecer o `LEFT JOIN`, que
mostra também quem ficou sem par.

## Agora é sua vez

Monte o **catálogo**: o **título** de cada livro e o **nome do autor**.

**Desafio extra (opcional, não vale para concluir a aula):** mostre só os livros de autores do
**Brasil**. Devem vir 4 livros: Dom Casmurro, Memórias Póstumas de Brás Cubas, A Hora da Estrela
e Laços de Família.

## Resumo

- Dados relacionados ficam em tabelas separadas, ligadas por uma coluna de número (`autor_id` → `autores.id`).
- `INNER JOIN tabela ON condição` junta as tabelas dizendo como as linhas se casam.
- Use `tabela.coluna` ou apelidos (`livros AS l`) quando o mesmo nome de coluna existir nas duas tabelas.
- `INNER JOIN` só mostra as linhas que têm par nas duas tabelas.
