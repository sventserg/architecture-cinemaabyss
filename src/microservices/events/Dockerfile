FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8082
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    iputils-ping \
    netcat-openbsd && \
    rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "Events.dll"]