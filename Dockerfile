# https://hub.docker.com/_/microsoft-dotnet-core
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
COPY . /src
WORKDIR /src
RUN dotnet publish ./src/GprTool/GprTool.csproj -c Release -o publish
RUN dotnet exec publish/gpr.dll

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS production
LABEL maintainer="jcansdale@gmail.com"
WORKDIR /static
COPY --from=build /src/publish .
ENTRYPOINT [ "dotnet", "exec", "gpr.dll" ]
