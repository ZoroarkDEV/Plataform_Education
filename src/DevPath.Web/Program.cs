using DevPath.Application.Content;
using DevPath.Infrastructure;
using DevPath.Infrastructure.Content;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Banco de dados e demais serviços de infraestrutura (connection string via ConnectionStrings__DevPath).
builder.Services.AddInfrastructure(builder.Configuration);

// Cookie de autenticação do Identity. Fica na Web porque depende das rotas do AccountController.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".DevPath.Auth";
    options.Cookie.HttpOnly = true;                       // JavaScript não lê o cookie (protege contra XSS)
    options.Cookie.SameSite = SameSiteMode.Lax;           // não vai em POSTs vindos de outros sites (CSRF)
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTP no dev local; use Always atrás de HTTPS em produção
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;                     // renova enquanto o usuário estiver ativo
});

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
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
