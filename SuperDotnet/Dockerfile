ARG LAUNCHING_FROM_VS
# Isso define a imagem base definitiva, mas somente se LAUNCHING_FROM_VS tiver sido definido
ARG FINAL_BASE_IMAGE=${LAUNCHING_FROM_VS:+aotdebug}

# Esta fase é usada durante a execução no VS no modo rápido (Padrão para a configuração de Depuração)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 9999


# Esta fase é usada para compilar o projeto de serviço
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Instalar dependências clang/zlib1g-dev para publicação no nativo
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    clang zlib1g-dev \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SuperDotnet.csproj", "./"]
RUN dotnet restore "./SuperDotnet.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "./SuperDotnet.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Esta fase é usada para publicar o projeto de serviço a ser copiado para a fase final
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SuperDotnet.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# Esta fase é usada como base para a fase final ao iniciar no VS para dar suporte à depuração no modo normal (Padrão ao não usar a configuração de Depuração)
FROM base AS aotdebug
USER root
# Instalar o GDB para dar suporte à depuração nativa
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    gdb \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
USER app

# Esta fase é usada na produção ou quando executada no VS no modo normal (padrão quando não está usando a configuração de Depuração)
FROM ${FINAL_BASE_IMAGE:-mcr.microsoft.com/dotnet/aspnet:10.0} AS final
WORKDIR /app
EXPOSE 9999
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","SuperDotnet.dll"]