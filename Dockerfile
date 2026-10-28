FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Burrfish.Domain/Burrfish.Domain.csproj src/Burrfish.Domain/
COPY src/Burrfish.Application/Burrfish.Application.csproj src/Burrfish.Application/
COPY src/Burrfish.Infrastructure/Burrfish.Infrastructure.csproj src/Burrfish.Infrastructure/
COPY src/Burrfish.Api/Burrfish.Api.csproj src/Burrfish.Api/
RUN dotnet restore src/Burrfish.Api/Burrfish.Api.csproj

COPY src/ src/
RUN dotnet publish src/Burrfish.Api/Burrfish.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Burrfish.Api.dll"]
