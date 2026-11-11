# Etapa 1: compilar con el SDK de .NET 10 (nativo, sin emulación)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/Rehab.Api/Rehab.Api.csproj src/Rehab.Api/
RUN dotnet restore src/Rehab.Api/Rehab.Api.csproj -a $TARGETARCH
COPY src/Rehab.Api/ src/Rehab.Api/
RUN dotnet publish src/Rehab.Api/Rehab.Api.csproj -c Release -a $TARGETARCH -o /app --no-restore

# Etapa 2: imagen final solo con el runtime de ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rehab.Api.dll"]
