# Conteúdo pedagógico

Todo o conteúdo das trilhas fica aqui, fora do código (README §8, ADR-003). O `ContentSeeder`
lê esta pasta e grava trilhas, módulos e aulas no banco. Rodar o seed de novo atualiza o que mudou
e **não duplica** nada.

## Estrutura

```
content/
└── paths/
    └── <slug-da-trilha>/            ← slug da trilha (ex.: sql-dados); não muda com o título
        ├── path.yaml                ← metadados da trilha + lista ordenada de módulos
        └── modules/
            └── <slug-do-modulo>/    ← um slug listado em path.yaml
                └── NN-<slug>.md     ← aula: NN = order, <slug> = slug da aula (ex.: 02-joins.md)
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
---

# O que é um banco relacional
...
```

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

Erros de YAML apontam arquivo e linha, por exemplo:
`content/paths/sql-dados/modules/consultas/01-joins.md, linha 4: chave desconhecida 'ordem'. Chaves aceitas: ...`.
Se houver qualquer erro, o seed não grava nada (nem as partes válidas).

Rode `dotnet test tests/DevPath.ContentTests` depois de editar qualquer arquivo daqui.

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
  - Aula removida: vai para o fim da ordem do módulo (ainda não há como despublicar uma aula no
    domínio; isso depende de um campo novo em `Lesson`).
  - O log mostra um aviso para cada item órfão. Se o item voltar para cá com o mesmo slug, ele é
    reativado/republicado na posição do arquivo.

## Quando o seed roda

Só no ambiente `Development` e com `Content:SeedOnStartup` = `true` (padrão em
`appsettings.Development.json`). `dotnet run` usa o perfil do `launchSettings.json`, que já define
`ASPNETCORE_ENVIRONMENT=Development`. Em produção o seed não roda na inicialização.
