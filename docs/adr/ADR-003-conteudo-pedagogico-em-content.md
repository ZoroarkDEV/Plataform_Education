# ADR-003 — Conteúdo pedagógico em `/content`

Status: Accepted
Data: 2026-10-28

## Contexto

O conteúdo (aulas, exercícios, trilhas) é o coração da plataforma e muda num ritmo diferente do
código. O README §3.2 pede conteúdo em **Markdown/JSON desacoplado do código**, o §3.3 proíbe
**hardcodar conteúdo pedagógico em Controllers ou Views**, e o §8 define que todo conteúdo fica em
`/content`. A SPEC §7.3 detalha o formato: Markdown versionado, frontmatter YAML com metadados,
exemplos de código embutidos e exercícios com test cases em JSON. A SPEC §3.2 sugeria um projeto
`src/DevPath.Content`, mas o README §6 (que tem prioridade) coloca o conteúdo na pasta `content/`
da raiz.

## Decisão

- Todo conteúdo pedagógico fica em **`/content`**, na raiz do repositório, versionado no Git.
- **Aulas:** arquivos Markdown com **frontmatter YAML** para os metadados (título, ordem, etc.) e
  exemplos de código em blocos cercados (```` ```csharp ````, ```` ```sql ````...).
- **Exercícios** têm, no mínimo (README §8): enunciado claro, starter code, **test cases em JSON** e
  solução protegida.
- O código **lê** o conteúdo de `/content` (ex.: para seed do banco e renderização das aulas);
  Controllers e Views nunca contêm texto de aula ou de exercício.
- Conteúdo pedagógico nunca é misturado com lógica de negócio.

## Consequências

- **Mais fácil:** atualizar uma aula é editar um arquivo e abrir um PR, com histórico e revisão no
  Git; o conteúdo pode ser validado por testes (SPEC §9.4: arquivos existem, test cases válidos,
  ordem dos módulos) e é reaproveitado no seed do banco.
- **Mais difícil:** é preciso um leitor/parser de Markdown + frontmatter e uma etapa de
  sincronização entre `/content` e o banco; erros de formato só aparecem se houver validação.
- **Em aberto:**
  - Esquema exato do frontmatter e dos JSON de exercícios: definir na issue #10 (seed das trilhas a
    partir de `/content`) e na #12 (renderização de aulas em Markdown).
  - Como a "solução protegida" é guardada e escondida do aluno.
  - Organização interna das pastas (ex.: por trilha/módulo/aula), a ser decidida junto com o seed.
