# syntax=docker/dockerfile:1
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers are cached independently of source changes.
COPY src/StockPilot.Web/StockPilot.Web.csproj src/StockPilot.Web/
RUN dotnet restore src/StockPilot.Web/StockPilot.Web.csproj

COPY src/ src/
RUN dotnet publish src/StockPilot.Web/StockPilot.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .

# The base image ships an unprivileged "app" user.
USER $APP_UID

ENTRYPOINT ["dotnet", "StockPilot.Web.dll"]
