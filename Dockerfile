# https://hub.docker.com/_/microsoft-dotnet-core
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
COPY . .
RUN dotnet build ./src/GprTool/GprTool.csproj -c Release -o build

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS production
LABEL maintainer="jcansdale@gmail.com"
WORKDIR /tool
COPY --from=build /build .
ENTRYPOINT [ "dotnet", "gpr.dll" ]
