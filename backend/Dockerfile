# Build context: the backend/ folder.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DragonFit.Api/DragonFit.Api.csproj DragonFit.Api/
RUN dotnet restore DragonFit.Api/DragonFit.Api.csproj
COPY DragonFit.Api/ DragonFit.Api/
RUN dotnet publish DragonFit.Api/DragonFit.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
ENTRYPOINT ["dotnet", "DragonFit.Api.dll"]
