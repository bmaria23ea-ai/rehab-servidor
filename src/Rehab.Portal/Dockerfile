# Etapa 1: compilar con el SDK de .NET 10 (nativo, sin emulación)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/Rehab.Portal/Rehab.Portal.csproj src/Rehab.Portal/
RUN dotnet restore src/Rehab.Portal/Rehab.Portal.csproj -a $TARGETARCH
COPY src/Rehab.Portal/ src/Rehab.Portal/
RUN dotnet publish src/Rehab.Portal/Rehab.Portal.csproj -c Release -a $TARGETARCH -o /app --no-restore

# Etapa 2: imagen final solo con el runtime de ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rehab.Portal.dll"]
