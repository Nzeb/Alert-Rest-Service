FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Alert-Rest-API.csproj", "./"]
RUN dotnet restore "./Alert-Rest-API.csproj"

COPY . .
RUN dotnet publish "./Alert-Rest-API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV DISCORD_WEBHOOK_URL=https://example.invalid/webhook

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Alert-Rest-API.dll"]
