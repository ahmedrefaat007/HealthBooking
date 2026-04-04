FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "src/ApiGateway/HealthBooking.ApiGateway/HealthBooking.ApiGateway.csproj"

RUN dotnet publish "src/ApiGateway/HealthBooking.ApiGateway/HealthBooking.ApiGateway.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HealthBooking.ApiGateway.dll"]
