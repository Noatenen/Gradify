# שלב 1 – build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . ./
RUN dotnet publish Server/AuthWithAdmin.Server.csproj -c Release -o out

# שלב 2 – run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/out ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "AuthWithAdmin.Server.dll"]