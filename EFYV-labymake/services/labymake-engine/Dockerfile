FROM mcr.microsoft.com/dotnet/sdk:8.0.423 AS build
WORKDIR /src
COPY ["Shared/EFYV.Runtime.Media/EFYV.Runtime.Media.csproj", "Shared/EFYV.Runtime.Media/"]
COPY ["Shared/EFYV.Runtime.Media/Directory.Build.props", "Shared/EFYV.Runtime.Media/"]
COPY ["Shared/EFYV.Labyrinth.Artifacts/EFYV.Labyrinth.Artifacts.csproj", "Shared/EFYV.Labyrinth.Artifacts/"]
COPY ["EFYV-labybackend/Core/EFYVBackend.Core.csproj", "EFYV-labybackend/Core/"]
COPY ["EFYV-labybackend/Core/Directory.Build.props", "EFYV-labybackend/Core/"]
COPY ["EFYV-labymake/services/labymake-engine/LabyMakeEngine.csproj", "EFYV-labymake/services/labymake-engine/"]
COPY ["EFYV-labymake/services/labymake-engine/packages.lock.json", "EFYV-labymake/services/labymake-engine/"]
RUN dotnet restore EFYV-labymake/services/labymake-engine/LabyMakeEngine.csproj --locked-mode
COPY Shared/EFYV.Runtime.Media/ Shared/EFYV.Runtime.Media/
COPY Shared/EFYV.Labyrinth.Artifacts/ Shared/EFYV.Labyrinth.Artifacts/
COPY EFYV-labybackend/Core/ EFYV-labybackend/Core/
COPY EFYV-labymake/services/labymake-engine/ EFYV-labymake/services/labymake-engine/
RUN dotnet publish EFYV-labymake/services/labymake-engine/LabyMakeEngine.csproj --configuration Release --output /out --no-self-contained --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0.29
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:50051
EXPOSE 50051
USER $APP_UID
ENTRYPOINT ["dotnet", "LabyMakeEngine.dll"]
