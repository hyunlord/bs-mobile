FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY core/src/SowSiege.Core core/src/SowSiege.Core
COPY core/src/SowSiege.Content core/src/SowSiege.Content
COPY server server
WORKDIR /source/server
RUN dotnet publish src/SowSiege.Server.Host -c Release -o /publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /publish .
COPY data/meta /content/data/meta
COPY data/opt-in/account-estate /content/data/opt-in/account-estate
COPY data/opt-in/slg/recruit-pool-v1.json data/opt-in/slg/content-draft-v1.json /content/data/opt-in/slg/
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 GameContentRoot=/content
ENTRYPOINT ["dotnet", "SowSiege.Server.Host.dll"]
