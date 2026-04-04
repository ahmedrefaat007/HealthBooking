FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5005

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "src/IdentityServer/HealthBooking.IdentityServer/HealthBooking.IdentityServer.csproj"

RUN dotnet publish "src/IdentityServer/HealthBooking.IdentityServer/HealthBooking.IdentityServer.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HealthBooking.IdentityServer.dll"]
