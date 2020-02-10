# https://hub.docker.com/_/microsoft-dotnet-core
FROM mcr.microsoft.com/dotnet/core/sdk:3.1

LABEL maintainer="jcansdale@gmail.com"

COPY . /src

WORKDIR /src

ENTRYPOINT [ "dotnet", "run", "--project", "./src/GprTool/" ]
