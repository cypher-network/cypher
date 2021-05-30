#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app

# api
EXPOSE 7000
# serf
EXPOSE 7946
# node monitor
EXPOSE 7005

# needed by rocksdb
RUN apt-get -y update && apt-get -y install --no-install-recommends libc6-dev=2.28-10 && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["cypnode/cypnode.csproj", "cypnode/"]
COPY ["cypcore/cypcore.csproj", "cypcore/"]
RUN dotnet restore "cypnode/cypnode.csproj"
COPY . .
WORKDIR /src/cypnode
RUN dotnet build "cypnode.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "cypnode.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "cypnode.dll"]