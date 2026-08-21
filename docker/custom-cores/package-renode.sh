#!/usr/bin/env bash

set -euo pipefail

cd "${RENODE_WORKDIR:-/workspace}"

BUILD_HOST_ARCH="${RENODE_BUILD_HOST_ARCH:-}"
if [[ -z "$BUILD_HOST_ARCH" ]]; then
    case "${RENODE_DOCKER_TARGETARCH:-$(uname -m)}" in
        amd64|x86_64)
            BUILD_HOST_ARCH="i386"
            ;;
        arm64|aarch64)
            BUILD_HOST_ARCH="aarch64"
            ;;
    esac
fi

BUILD_ARGS=(-p)
if [[ "${RENODE_PACKAGE_SKIP_FETCH:-true}" != "false" ]]; then
    BUILD_ARGS+=(--skip-fetch)
fi
if [[ -n "$BUILD_HOST_ARCH" ]]; then
    BUILD_ARGS+=(--host-arch "$BUILD_HOST_ARCH")
fi

./build.sh "${BUILD_ARGS[@]}"

mkdir -p output/deploy
find output/packages -maxdepth 1 -type f -name "*.deb" -exec cp -v {} output/deploy/ \;
ls -l output/deploy/*.deb
