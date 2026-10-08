FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG PROJECT=NexoRuta.Backoffice

WORKDIR /src
COPY . .

RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app/publish /p:UseAppHost=false
RUN case "$PROJECT" in NexoRuta.Commerce|NexoRuta.Backoffice) ;; *) exit 1 ;; esac \
    && printf '#!/bin/sh\nexec dotnet %s.dll\n' "$PROJECT" > /app/publish/start-web

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["sh", "/app/start-web"]
