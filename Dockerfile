FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app
COPY . .

RUN dotnet restore
RUN dotnet build -c Release --no-restore
RUN dotnet publish -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:7.0

WORKDIR /app

COPY --from=build /app/publish .

COPY version.txt /app/version.txt

RUN apt-get update && apt-get install -y curl libfreetype6 libfontconfig1 fontconfig
RUN apt clean
RUN apt autoremove -y
RUN rm -rf /etc/apt/sources.list
RUN rm -rf /etc/apt/sources.list.d/*

HEALTHCHECK --interval=30s --timeout=5s --retries=5 --start-period=2s CMD curl -s --fail http://localhost:5000 || exit 1

ENTRYPOINT ["dotnet", "liebo.dll"]
