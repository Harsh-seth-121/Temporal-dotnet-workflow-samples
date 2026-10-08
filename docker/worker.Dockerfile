# syntax=docker/dockerfile:1
#
# Build context is the repository root, not this directory:
#   docker build -f docker/worker.Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Docker reports amd64 where .NET says x64. Everything else lines up, so arm64
# passes straight through. Deriving the architecture from the build rather than
# hardcoding it keeps the image correct when built for another platform: a
# hardcoded RID would pair an arm64 native library with an amd64 runtime and die
# at startup with DllNotFoundException.
ARG TARGETARCH
RUN arch="${TARGETARCH:-$(dpkg --print-architecture)}" \
    && case "$arch" in \
         amd64|x86_64)  arch=x64 ;; \
         arm64|aarch64) arch=arm64 ;; \
         386|i386)      arch=x86 ;; \
         arm|armhf|armel|arm/v7|arm/v6) arch=arm ;; \
         *) echo "no .NET architecture mapping for '$arch'" >&2; exit 1 ;; \
       esac \
    && echo "$arch" > /tmp/dotnet-arch \
    && echo "building for .NET architecture: $arch"

# Project files first so the restore layer survives source edits.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Sandbox.Abstractions/Sandbox.Abstractions.csproj   src/Sandbox.Abstractions/
COPY src/Sandbox.Samples.Hello/Sandbox.Samples.Hello.csproj src/Sandbox.Samples.Hello/
COPY src/Sandbox.Worker/Sandbox.Worker.csproj               src/Sandbox.Worker/
RUN dotnet restore src/Sandbox.Worker -a "$(cat /tmp/dotnet-arch)"

COPY .editorconfig ./
COPY src/ src/

# Publishing for one runtime identifier ships a single copy of the native core.
# The package carries one roughly 28 MB library per platform across eight
# platforms, and a portable publish would copy all of them.
RUN dotnet publish src/Sandbox.Worker \
        -c Release \
        -a "$(cat /tmp/dotnet-arch)" \
        --self-contained false \
        --no-restore \
        -p:DebugType=none \
        -p:DebugSymbols=false \
        -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
RUN useradd --create-home --uid 10001 worker
WORKDIR /app
COPY --from=build --chown=worker:worker /app ./
USER 10001

# No shell wrapper: dotnet must be PID 1 so SIGTERM reaches the host and
# activities drain instead of being killed when the stop timeout expires.
ENTRYPOINT ["dotnet", "Sandbox.Worker.dll"]
