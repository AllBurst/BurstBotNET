FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["BurstBotNET/BurstBotNET.csproj", "BurstBotNET/"]
RUN dotnet restore "BurstBotNET/BurstBotNET.csproj"
COPY . .
WORKDIR "/src/BurstBotNET"
RUN dotnet build "BurstBotNET.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BurstBotNET.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BurstBotNET.dll"]
