using DevPath.Application.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.Infrastructure.Content;

public static class ContentServiceCollectionExtensions
{
    /// <summary>
    /// Registra a leitura de <c>/content</c> e o <see cref="IContentSeeder"/>.
    /// </summary>
    /// <param name="startDirectory">
    /// De onde procurar a pasta <c>content/</c> (no Web, o ContentRootPath). Pode ser sobrescrito por
    /// <c>Content:RootPath</c> (variável de ambiente <c>Content__RootPath</c>).
    /// </param>
    public static IServiceCollection AddContentSeeding(
        this IServiceCollection services, IConfiguration configuration, string startDirectory)
    {
        // A pasta só é procurada quando alguém pede o conteúdo, não no startup.
        services.AddSingleton<IContentSource>(_ =>
            new FileSystemContentSource(ContentRootLocator.Resolve(configuration["Content:RootPath"], startDirectory)));

        services.AddScoped<IContentSeeder, ContentSeeder>();

        return services;
    }
}
