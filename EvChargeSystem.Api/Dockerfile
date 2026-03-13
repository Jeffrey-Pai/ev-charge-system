FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["EvChargeSystem.Api/EvChargeSystem.Api.csproj", "EvChargeSystem.Api/"]
RUN dotnet restore "EvChargeSystem.Api/EvChargeSystem.Api.csproj"

COPY . .
WORKDIR "/src/EvChargeSystem.Api"
RUN dotnet publish "EvChargeSystem.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "EvChargeSystem.Api.dll"]
