using System.Net;
using DevPath.Application.Content;
using DevPath.Infrastructure;
using DevPath.Infrastructure.Content;
using DevPath.Web.Lessons;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Banco de dados e demais serviços de infraestrutura (connection string via ConnectionStrings__DevPath).
builder.Services.AddInfrastructure(builder.Configuration);

// Tela de aula (#12): Markdown -> HTML seguro (Markdig com DisableHtml + destaque SQL/C#).
// O ILessonContentService real (banco) vem de AddInfrastructure.
builder.Services.AddSingleton<LessonMarkdownRenderer>();

// Cookie de autenticação do Identity. Fica na Web porque depende das rotas do AccountController.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".DevPath.Auth";
    options.Cookie.HttpOnly = true;                       // JavaScript não lê o cookie (protege contra XSS)
    options.Cookie.SameSite = SameSiteMode.Lax;           // não vai em POSTs vindos de outros sites (CSRF)
    // Fora do Development o cookie só trafega em HTTPS. No dev local (http://localhost) seria descartado.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;                     // renova enquanto o usuário estiver ativo
});

// Atrás de um proxy reverso (nginx, Traefik, load balancer) o HTTPS termina no proxy e a app recebe HTTP.
// Os cabeçalhos X-Forwarded-Proto/For contam o esquema e o IP originais. Só confiamos neles quando
// vêm de um proxy conhecido; senão, qualquer cliente poderia "fingir" HTTPS ou outro IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Padrão do ASP.NET Core: só loopback (127.0.0.1/::1). Para outro proxy, configure
    // ForwardedHeaders__KnownProxies__0=10.0.0.5 (um item por IP).
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }

    // Em redes Docker o IP do proxy muda a cada deploy. ForwardedHeaders__TrustAllProxies=true limpa as
    // listas e aceita os cabeçalhos de qualquer origem. Use SÓ se a app não estiver exposta diretamente
    // (apenas o proxy alcança a porta dela).
    if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

// Porta do HTTPS "público" para o redirecionamento HTTP -> HTTPS. Atrás de proxy a app não escuta
// em HTTPS, então não dá para descobrir sozinha (era o log "Failed to determine the https port").
// Configure com HTTPS_PORT ou ASPNETCORE_HTTPS_PORT; o padrão é 443.
builder.Services.AddHttpsRedirection(options =>
    options.HttpsPort = builder.Configuration.GetValue<int?>("HTTPS_PORT") ?? 443);

// Leitura do conteúdo pedagógico de /content e o seed das trilhas (issue #10).
builder.Services.AddContentSeeding(builder.Configuration, builder.Environment.ContentRootPath);

var app = builder.Build();

// Em Development, importa /content para o banco no startup (upsert: rodar de novo não duplica).
// Desligue com Content__SeedOnStartup=false. Precisa das migrations aplicadas.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Content:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<IContentSeeder>().SeedAsync();
    }
    catch (Exception ex)
    {
        // Não derruba a aplicação: só avisa no log o que precisa ser corrigido.
        app.Logger.LogError(ex, "Seed de conteúdo não executado: {Message}", ex.Message);
    }
}

// Primeiro de tudo: corrige Request.Scheme/RemoteIpAddress antes de cookie, HSTS e redirecionamento.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRouting();

// Ordem importa: primeiro descobre QUEM é o usuário (cookie), depois verifica O QUE ele pode acessar.
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

// Torna a classe Program visível para o WebApplicationFactory<Program> dos testes de integração.
public partial class Program;
