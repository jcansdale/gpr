# https://hub.docker.com/_/microsoft-dotnet-core
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
COPY . .
RUN dotnet publish ./src/GprTool/GprTool.csproj -c Release -o publish

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS production
LABEL maintainer="jcansdale@gmail.com"
WORKDIR /tool
COPY --from=build /publish .
ENTRYPOINT [ "./gpr" ]
