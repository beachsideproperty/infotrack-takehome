# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between code changes.
COPY global.json Directory.Build.props BookFinder.sln ./
COPY src/BookFinder.Api/BookFinder.Api.csproj src/BookFinder.Api/
COPY src/BookFinder.Application/BookFinder.Application.csproj src/BookFinder.Application/
COPY src/BookFinder.Domain/BookFinder.Domain.csproj src/BookFinder.Domain/
COPY src/BookFinder.Infrastructure/BookFinder.Infrastructure.csproj src/BookFinder.Infrastructure/
RUN dotnet restore src/BookFinder.Api/BookFinder.Api.csproj

COPY src/ src/
RUN dotnet publish src/BookFinder.Api/BookFinder.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Render's default port; Program.cs binds 0.0.0.0:$PORT.
ENV PORT=10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 10000

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BookFinder.Api.dll"]
