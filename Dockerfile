# https://hub.docker.com/_/microsoft-dotnet-core
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
COPY . /src
WORKDIR /src
RUN dotnet build -c Release
RUN dotnet exec src/GprTool/bin/Release/netcoreapp3.0/gpr.dll

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS production
LABEL maintainer="jcansdale@gmail.com"
COPY . /src
WORKDIR /static
COPY --from=build /src/src/GprTool/bin/Release/netcoreapp3.0 .
ENTRYPOINT [ "dotnet", "exec", "gpr.dll" ]
