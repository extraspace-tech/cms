# Base images are pinned by tag and multi-arch index digest; Dependabot raises both together.

# 1. The admin UI. The build writes to ../src/ExtraSpace.Api/wwwroot/admin, so the stage keeps the repo layout.
FROM node:24.18.0-bookworm-slim@sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d AS ui
WORKDIR /repo/admin-ui
COPY admin-ui/package.json admin-ui/package-lock.json ./
RUN npm ci
COPY admin-ui/ ./
RUN npm run build

# 2. The API.
FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
# src/Directory.Build.props adds an analyzer package, so restore must see it.
COPY src/Directory.Build.props src/
COPY src/ExtraSpace.Api/ExtraSpace.Api.csproj src/ExtraSpace.Api/
COPY src/ExtraSpace.Engines/ExtraSpace.Engines.csproj src/ExtraSpace.Engines/
RUN dotnet restore src/ExtraSpace.Api/ExtraSpace.Api.csproj
COPY src/ src/
COPY --from=ui /repo/src/ExtraSpace.Api/wwwroot/admin src/ExtraSpace.Api/wwwroot/admin
# No Development settings in the image: a task set to Development by mistake must not switch email to the console log.
RUN dotnet publish src/ExtraSpace.Api/ExtraSpace.Api.csproj -c Release -o /app --no-restore \
    && rm /app/appsettings.Development.json

# 3. Runtime. The full noble image: the jobs role needs tzdata (Asia/Singapore) and ICU.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac
WORKDIR /app
COPY --from=build /app ./
# After the COPY, so a new commit SHA does not rebuild the app layer.
ARG VERSION=dev
ENV App__Version=$VERSION
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExtraSpace.Api.dll"]
