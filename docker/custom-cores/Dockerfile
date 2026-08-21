FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy

ARG DEBIAN_FRONTEND=noninteractive
ARG BUILDPLATFORM
ARG TARGETOS
ARG TARGETARCH

LABEL org.opencontainers.image.title="Renode custom cores offline builder" \
      org.opencontainers.image.description="Offline-capable Renode custom tlib core audit and build image"

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        bash \
        ca-certificates \
        cmake \
        coreutils \
        g++ \
        gcc \
        git \
        libarchive-tools \
        make \
        pkg-config \
        python3 \
        python3-pytest \
        rpm \
        ruby \
        ruby-dev \
        xz-utils \
    && gem install --no-document fpm \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    RENODE_DOCKER_TARGETOS=${TARGETOS} \
    RENODE_DOCKER_TARGETARCH=${TARGETARCH}

WORKDIR /workspace

COPY run-custom-cores.sh /usr/local/bin/run-custom-cores
COPY package-renode.sh /usr/local/bin/package-renode
RUN chmod +x /usr/local/bin/run-custom-cores /usr/local/bin/package-renode

ENTRYPOINT ["/usr/local/bin/run-custom-cores"]
