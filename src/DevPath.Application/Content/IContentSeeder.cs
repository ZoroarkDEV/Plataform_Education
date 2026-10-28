namespace DevPath.Application.Content;

/// <summary>Importa o conteúdo de <c>/content</c> para o banco (upsert idempotente).</summary>
public interface IContentSeeder
{
    Task<ContentSeedResult> SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Quantas linhas o seed inseriu e atualizou. Rodar duas vezes seguidas dá 0 e 0 na segunda.</summary>
public sealed record ContentSeedResult(int Inserted, int Updated)
{
    public bool NothingChanged => Inserted == 0 && Updated == 0;
}
