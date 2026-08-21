#!/usr/bin/env bash

set -euo pipefail

cd "${RENODE_WORKDIR:-/workspace}"

ARCHES="${RENODE_CUSTOM_CORE_ARCHES:-avr stm8 mcs51 pic16 pic18 c2000 dspic33}"
FAIL_ON="${RENODE_CUSTOM_CORES_AUDIT_FAIL_ON:-error}"
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

BUILD_ARGS=()
if [[ -n "$BUILD_HOST_ARCH" ]]; then
    BUILD_ARGS+=(--host-arch "$BUILD_HOST_ARCH")
fi

python3 -m pytest -q tests/custom_cores_audit
python3 tools/custom_cores_audit/audit_repo.py . --fail-on "$FAIL_ON"

for arch in $ARCHES; do
    echo "Building custom tlib core: ${arch}"
    RENODE_CUSTOM_CORES="${arch}.le" ./build.sh \
        --external-lib-only \
        --external-lib-arch "$arch" \
        --tlib-export-compile-commands \
        --skip-fetch \
        "${BUILD_ARGS[@]}"
done
