# https://hub.docker.com/_/microsoft-dotnet-core
FROM mcr.microsoft.com/dotnet/core/sdk:3.1.401 AS build
COPY . .
RUN dotnet publish ./src/GprTool/GprTool.csproj -c Release -o publish

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 AS production
LABEL maintainer="jcansdale@gmail.com"
LABEL org.opencontainers.image.source https://github.com/jcansdale/gpr
WORKDIR /tool
COPY --from=build /publish .
ARG READ_PACKAGES_TOKEN
ENV READ_PACKAGES_TOKEN=$READ_PACKAGES_TOKEN
ENTRYPOINT [ "./gpr" ]
