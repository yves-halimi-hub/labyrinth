FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish LabyMakeEngine.csproj --configuration Release --output /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:50051
EXPOSE 50051
USER $APP_UID
ENTRYPOINT ["dotnet", "LabyMakeEngine.dll"]
