FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG PROJECT=NexoRuta.Backoffice

WORKDIR /src
COPY . .

RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "NexoRuta.Backoffice.dll"]