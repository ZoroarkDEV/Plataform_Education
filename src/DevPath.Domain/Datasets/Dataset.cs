using System.Security.Cryptography;
using System.Text;
using DevPath.Domain.Common;

namespace DevPath.Domain.Datasets;

/// <summary>
/// Um conjunto de dados de exercício (ex.: "livraria"): o script SQL que cria e preenche as tabelas
/// que o aluno vai consultar. Vem de <c>content/datasets/&lt;slug&gt;.sql</c> e o seed grava aqui.
/// </summary>
/// <remarks>
/// Vários exercícios usam o mesmo dataset; cada caso de teste aponta para ele pelo <see cref="Slug"/>.
/// O <see cref="ContentHash"/> (SHA-256 do script) deixa o seed saber se o arquivo mudou sem comparar o texto todo.
/// </remarks>
public sealed class Dataset
{
    public Guid Id { get; private set; }

    /// <summary>Identificador único e estável, igual ao nome do arquivo (ex.: "livraria").</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Script SQL confiável (escrito pela equipe), executado como superusuário dentro do sandbox.</summary>
    public string Sql { get; private set; } = string.Empty;

    /// <summary>SHA-256 do <see cref="Sql"/> em hexadecimal minúsculo (64 caracteres).</summary>
    public string ContentHash { get; private set; } = string.Empty;

    private Dataset() { }

    /// <param name="slug">Ex.: "livraria". Normalizado pelo <see cref="SlugGenerator"/>.</param>
    /// <param name="sql">Script com CREATE TABLE + INSERT.</param>
    public static Dataset Create(string slug, string sql)
    {
        var dataset = new Dataset { Id = Guid.NewGuid(), Slug = SlugGenerator.Generate(slug) };
        dataset.UpdateSql(sql);
        return dataset;
    }

    /// <summary>Troca o script (ex.: o seed achou um arquivo novo). Devolve <c>true</c> se mudou algo.</summary>
    public bool UpdateSql(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var hash = ComputeHash(sql);
        if (hash == ContentHash)
        {
            return false;
        }

        Sql = sql;
        ContentHash = hash;
        return true;
    }

    /// <summary>SHA-256 do texto em UTF-8, em hexadecimal minúsculo. Público para o seed comparar com o arquivo.</summary>
    public static string ComputeHash(string sql) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
