# Imagem do DevPath.Worker (issue #25). Build a partir da RAIZ do repositório:
#   docker build -f src/DevPath.Worker/Dockerfile -t devpath-worker .

# 1) Compila com o SDK. Os .csproj vêm primeiro para o restore ficar em cache entre builds.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/DevPath.Domain/DevPath.Domain.csproj src/DevPath.Domain/
COPY src/DevPath.Application/DevPath.Application.csproj src/DevPath.Application/
COPY src/DevPath.Infrastructure/DevPath.Infrastructure.csproj src/DevPath.Infrastructure/
COPY src/DevPath.Worker/DevPath.Worker.csproj src/DevPath.Worker/
RUN dotnet restore src/DevPath.Worker/DevPath.Worker.csproj
COPY src/ src/
RUN dotnet publish src/DevPath.Worker/DevPath.Worker.csproj -c Release --no-restore -o /app

# 2) Roda com o runtime do ASP.NET (a Infrastructure usa Identity/EF, que vêm desse framework).
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
# Só o binário do cliente Docker: o Worker cria os containers de sandbox chamando "docker run".
# O daemon é o do host, alcançado pelo socket montado no docker-compose (veja o comentário de risco lá).
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
COPY --from=build /app .
# Roda como root DENTRO deste container porque o socket do Docker do host costuma ser root:docker 660.
# Isso não aumenta o risco real: quem fala com o socket já controla o host de qualquer jeito (ADR-006).
# O código do ALUNO nunca roda aqui: roda nos containers de sandbox (sem rede, sem root, read-only).
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "DevPath.Worker.dll"]
