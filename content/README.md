# Conteúdo pedagógico

Todo o conteúdo das trilhas fica aqui, fora do código (README §8, ADR-003). O `ContentSeeder`
lê esta pasta e grava trilhas, módulos e aulas no banco. Rodar o seed de novo atualiza o que mudou
e **não duplica** nada.

## Estrutura

```
content/
├── datasets/
│   └── <nome>.sql                   ← banco de exemplo dos exercícios SQL (ex.: livraria.sql)
└── paths/
    └── <slug-da-trilha>/            ← slug da trilha (ex.: sql-dados); não muda com o título
        ├── path.yaml                ← metadados da trilha + lista ordenada de módulos
        └── modules/
            └── <slug-do-modulo>/    ← um slug listado em path.yaml
                ├── NN-<slug>.md            ← aula: NN = order, <slug> = slug da aula (ex.: 02-joins.md)
                └── NN-<slug>.exercise.json ← opcional: exercício da aula (mesmo nome da aula)
```

### `path.yaml`

```yaml
title: "SQL & Dados"
description: "..."
icon: database
difficulty_level: beginner          # beginner | intermediate | advanced
estimated_hours: 40
order: 2                            # posição da trilha na lista
modules:
  - slug: ambiente                  # nome da pasta em modules/
    title: "Ambiente: banco relacional + Docker"
    description: "..."
    order: 1                        # 1, 2, 3... sem buracos nem repetição
```

### Aula (`modules/<modulo>/NN-<aula>.md`)

```markdown
---
title: "O que é um banco relacional"
module: ambiente                    # precisa ser o slug da pasta do módulo
order: 1                            # 1, 2, 3... dentro do módulo
estimated_minutes: 20
language: sql                       # opcional: csharp, sql, bash, yaml...
difficulty: beginner                # beginner | intermediate | advanced
objectives:                         # opcional: o que o aluno vai aprender (mesma lista da seção da aula)
  - "Entender o que é uma tabela"
  - "Diferenciar linha e coluna"
draft: true                         # opcional (padrão false): rascunho, aparece como "Em breve"
---

# O que é um banco relacional
...
```

#### Chaves opcionais `objectives` e `draft`

- **`objectives`**: lista de frases curtas em português, cada uma com até 200 caracteres e sem item
  vazio (espaços nas pontas são removidos). Deve ser a mesma lista da seção
  `## O que você vai aprender` da aula (o teste de conteúdo compara as duas, ignorando `código`,
  **negrito** e ponto final). Toda aula pronta (sem `draft: true`) precisa ter `objectives`.
  A tela da aula esconde do corpo a seção `## O que você vai aprender` até o próximo `## ` e mostra
  `objectives` no lugar, então a seção tem **só a lista** e vem seguida direto de outro título `## `
  (há um teste de conteúdo para isso).
  O seed grava em `Lesson.LearningObjectives` (jsonb).
- **`draft: true`**: marca a aula como rascunho. A plataforma mostra a aula como **"Em breve"**.
  Todo stub com "Aula em construção" precisa ter `draft: true`. Sem a chave, vale `false`.
  O seed grava em `Lesson.IsDraft`.

O seed grava as duas chaves a cada execução (migration `AddLessonObjectivesAndDraft`): tirar
`objectives` do frontmatter deixa a lista vazia, e tirar `draft` (ou `draft: false`) publica a aula como pronta.

### Exercício (`modules/<modulo>/NN-<aula>.exercise.json`)

Fica ao lado da aula, com o mesmo nome (`01-seu-primeiro-select.md` →
`01-seu-primeiro-select.exercise.json`). Chave desconhecida é erro, com arquivo e linha.

```json
{
  "title": "Lista de preços",
  "statement": "Mostre o **título** e o **preço** de todos os livros.",
  "language": "sql",
  "dataset": "livraria",
  "points": 10,
  "starter_code": "SELECT \nFROM livros\n",
  "solution": "SELECT titulo, preco FROM livros;",
  "hints": ["Primeira dica, mais geral.", "Segunda dica, quase a resposta."],
  "test_cases": [
    {
      "name": "Título e preço de todos os livros",
      "columns": ["titulo", "preco"],
      "rows": [["Dom Casmurro", 29.90], ["O Hobbit", 49.90]],
      "order_matters": false,
      "check_column_names": false
    }
  ],
  "common_errors": [
    { "example": "SELECT titulo FROM livros;", "message": "Faltou a coluna preco." }
  ]
}
```

| Chave | Obrigatória | O que é |
|---|---|---|
| `title`, `statement`, `language` | sim | título (até 200), enunciado em Markdown e linguagem (até 50) |
| `dataset` | não | nome de um arquivo em `content/datasets/` (sem `.sql`); o banco onde a consulta roda |
| `points` | não | pontos do exercício (padrão 10). **Usar dica não reduz os pontos.** |
| `starter_code` | não | código que já aparece no editor |
| `solution` | sim | **solução protegida**: nunca é mostrada ao aluno e **nunca** pode aparecer no texto da aula (ADR-003, teste de conteúdo) |
| `hints` | não | dicas em ordem, da mais geral para a mais direta |
| `test_cases` | sim, ao menos 1 | resultado esperado: `columns`, `rows` (cada linha com um valor por coluna: texto, número, `true`/`false` ou `null`), `order_matters` e `check_column_names` (padrão `false`) |
| `common_errors` | não | erros comuns: `example` (consulta errada) e `message` (explicação amigável) |

Como a resposta do aluno é comparada (regras do PO, issue #22):

- Compara o **resultado**, não o texto da consulta.
- Quantidade e ordem das colunas precisam bater; os **nomes** só contam com `check_column_names: true`.
- A ordem das linhas só conta com `order_matters: true` (use quando o enunciado pede ordenação).
- Números são comparados pelo valor (`29.9` = `29.90`); textos, exatamente.

### Exemplos SQL dentro das aulas

Todo bloco ` ```sql ` de uma aula com exercício SQL é **executado** no dataset pelos testes de
integração (Testcontainers), então ele precisa rodar sem erro. Modelos de sintaxe que não são
consultas de verdade (`SELECT colunas FROM tabela;`) começam com a linha
`-- Formato geral (modelo, não é uma consulta para rodar)` e são pulados. Os testes também rodam a
`solution` contra os `test_cases` e garantem que nenhum `common_errors.example` é aceito como certo.

## Regras validadas pelos testes de conteúdo (SPEC §9.4)

- Cada trilha tem `path.yaml`.
- Slugs (pasta da trilha, `slug` do módulo, nome do arquivo da aula sem o `NN-`) usam só letras
  minúsculas sem acento, números e hífens: `sql-dados`, `o-que-e-banco-relacional`. Sem repetição
  dentro do mesmo pai.
- O arquivo da aula se chama `NN-<slug>.md` e `NN` é igual ao `order` do frontmatter.
- Todo módulo de `path.yaml` tem pasta em `modules/` com pelo menos uma aula, e não existe pasta de módulo fora da lista.
- O `module` do frontmatter é igual à pasta onde a aula está.
- Ordem de trilhas, módulos e aulas: começa em 1, sem repetição e sem buracos.
- Campos obrigatórios preenchidos e `difficulty` com um valor válido.
- Tamanhos máximos (os mesmos do banco): `title` 200, `description` 2000, `icon` 100, `language` 50
  e slug 200 caracteres.
- Cada item de `objectives` tem de 1 a 200 caracteres. Aula pronta tem `objectives`; stub tem `draft: true`.
- Exercício: JSON válido, sem chave desconhecida, com `solution` e ao menos um caso de teste; cada
  linha de `rows` tem um valor por coluna; o `dataset` existe; não há `.exercise.json` sem a aula.

Erros de YAML apontam arquivo e linha, por exemplo:
`content/paths/sql-dados/modules/consultas/01-joins.md, linha 4: chave desconhecida 'ordem'. Chaves aceitas: ...`.
Se houver qualquer erro, o seed não grava nada (nem as partes válidas).

Rode `dotnet test tests/DevPath.ContentTests` depois de editar qualquer arquivo daqui.
Se mexer em exercício SQL, dataset ou exemplo SQL de aula, rode também
`dotnet test tests/DevPath.IntegrationTests --filter SqlExercisesTests` (precisa do Docker).

## Como o seed identifica cada item

Cada item é encontrado no banco pelo **slug**, nunca pela ordem nem pelo título. O progresso dos
alunos fica preso à linha do banco, então o slug é a "identidade" do conteúdo:

| Item   | Slug vem de                                        |
|--------|----------------------------------------------------|
| Trilha | nome da pasta em `content/paths/`                  |
| Módulo | `slug` no `path.yaml` (igual ao nome da pasta)     |
| Aula   | nome do arquivo sem o prefixo (`02-joins.md` -> `joins`) |

### O que acontece quando você...

- **Muda o título, a descrição ou o texto**: a mesma linha é atualizada. O progresso continua.
- **Reordena aulas ou módulos** (ex.: insere uma aula nova no início e renumera as outras): renomeie
  só o prefixo `NN-` e o `order`, mantendo o slug. O seed atualiza a ordem das linhas existentes
  (em dois passos, por causa do índice único de ordem) e o progresso continua nas mesmas aulas.
- **Renomeia o slug** (pasta ou nome do arquivo): para o seed é um item **novo**. O antigo vira órfão
  (abaixo) e o progresso dos alunos fica no antigo. Só renomeie slug de propósito.
- **Remove um item** daqui: nada é apagado do banco.
  - Trilha removida: fica **desativada**.
  - Módulo removido: fica **despublicado** e vai para o fim da ordem da trilha.
  - Aula removida: fica **despublicada** (some da navegação e não abre) e vai para o fim da ordem
    do módulo. O progresso dos alunos continua preso a ela.
  - O log mostra um aviso para cada item órfão. Se o item voltar para cá com o mesmo slug, ele é
    reativado/republicado na posição do arquivo.

## Quando o seed roda

Só no ambiente `Development` e com `Content:SeedOnStartup` = `true` (padrão em
`appsettings.Development.json`). `dotnet run` usa o perfil do `launchSettings.json`, que já define
`ASPNETCORE_ENVIRONMENT=Development`. Em produção o seed não roda na inicialização.
