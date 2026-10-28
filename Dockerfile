# syntax=docker/dockerfile:1
# Imagem da aplicação DevPath.Web (issue #21). Uso normal: `docker compose up --build`.
# Multi-stage: o SDK compila e publica; a imagem final tem só o runtime ASP.NET.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Primeiro só os .csproj: o `dotnet restore` fica em cache enquanto as dependências não mudarem.
COPY src/DevPath.Domain/DevPath.Domain.csproj src/DevPath.Domain/
COPY src/DevPath.Application/DevPath.Application.csproj src/DevPath.Application/
COPY src/DevPath.Infrastructure/DevPath.Infrastructure.csproj src/DevPath.Infrastructure/
COPY src/DevPath.Web/DevPath.Web.csproj src/DevPath.Web/
RUN dotnet restore src/DevPath.Web/DevPath.Web.csproj

COPY src/ src/
# TailwindBuild=false: usa o wwwroot/css/site.css já commitado (sem baixar o CLI do Tailwind no build).
RUN dotnet publish src/DevPath.Web/DevPath.Web.csproj \
        --configuration Release --no-restore \
        -p:TailwindBuild=false -p:UseAppHost=false \
        --output /app/publish

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Usuário não-root que já vem na imagem oficial (APP_UID=1654, HOME=/home/app).
# A pasta das chaves do Data Protection é criada aqui para o volume do compose herdar o dono certo.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R "$APP_UID" /home/app/.aspnet

COPY --from=build /app/publish .
# Conteúdo pedagógico (trilhas, aulas, exercícios, datasets) vai dentro da imagem, somente leitura.
# O seed de Development lê daqui. Para editar conteúdo sem rebuild, monte ./content em /app/content.
COPY content/ /app/content/

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Content__RootPath=/app/content \
    DOTNET_NOLOGO=1
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "DevPath.Web.dll"]
