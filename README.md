# Alert REST API

Minimal ASP.NET Core Web API exposing one endpoint:

- POST /notification

## Run On Linux

Prerequisites:

- .NET 10 SDK (or compatible newer SDK/runtime)

```bash
dotnet restore
export DISCORD_WEBHOOK_URL="<your_discord_webhook_url>"
dotnet run --urls http://0.0.0.0:8080
```

## Run With Docker

Build image:

```bash
docker build -t alert-rest-api .
```

Run container:

```bash
docker run --rm -p 8080:8080 \
	-e DISCORD_WEBHOOK_URL="<your_discord_webhook_url>" \
	alert-rest-api
```

## Endpoint

`POST /notification`
