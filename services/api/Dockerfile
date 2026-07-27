FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Kreyora.slnx ./
COPY src/Kreyora.Domain/Kreyora.Domain.csproj src/Kreyora.Domain/
COPY src/Kreyora.Application/Kreyora.Application.csproj src/Kreyora.Application/
COPY src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj src/Kreyora.Infrastructure/
COPY src/Kreyora.ServiceDefaults/Kreyora.ServiceDefaults.csproj src/Kreyora.ServiceDefaults/
COPY src/Kreyora.WebApi/Kreyora.WebApi.csproj src/Kreyora.WebApi/
COPY src/Kreyora.AppHost/Kreyora.AppHost.csproj src/Kreyora.AppHost/

RUN dotnet restore src/Kreyora.WebApi/Kreyora.WebApi.csproj

COPY src/ src/

RUN dotnet publish src/Kreyora.WebApi/Kreyora.WebApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser

COPY --from=build /app/publish .

USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Kreyora.WebApi.dll"]
