# Conteúdo pedagógico

Todo o conteúdo das trilhas fica aqui, fora do código (README §8, ADR-003). O `ContentSeeder`
lê esta pasta e grava trilhas, módulos e aulas no banco. Rodar o seed de novo atualiza o que mudou
e **não duplica** nada.

## Estrutura

```
content/
└── paths/
    └── <slug-da-trilha>/            ← slug do título (ex.: "SQL & Dados" -> sql-dados)
        ├── path.yaml                ← metadados da trilha + lista ordenada de módulos
        └── modules/
            └── <slug-do-modulo>/    ← um slug listado em path.yaml
                └── 01-<aula>.md     ← aula em Markdown com frontmatter YAML
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

- Cada trilha tem `path.yaml` e a pasta tem o mesmo nome do slug do título.
- Todo módulo de `path.yaml` tem pasta em `modules/` com pelo menos uma aula, e não existe pasta de módulo fora da lista.
- O `module` do frontmatter é igual à pasta onde a aula está.
- Ordem de trilhas, módulos e aulas: começa em 1, sem repetição e sem buracos.
- Campos obrigatórios preenchidos e `difficulty` com um valor válido.

Rode `dotnet test tests/DevPath.ContentTests` depois de editar qualquer arquivo daqui.

## Como o seed identifica cada item

- Trilha: pelo **slug** (derivado do título).
- Módulo: pela **ordem** dentro da trilha. Aula: pela **ordem** dentro do módulo.

Por isso, mudar o título de uma aula atualiza a aula existente; reordenar aulas atualiza as linhas
pela nova ordem. O seed **não apaga** nada do banco: um arquivo removido daqui só gera um aviso no log.
