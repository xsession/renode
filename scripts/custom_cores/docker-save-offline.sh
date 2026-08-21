#!/usr/bin/env bash

set -euo pipefail

IMAGE_TAG="${1:-renode-custom-cores:offline}"
OUTPUT="${2:-renode-custom-cores-offline.tar}"
PLATFORM_ARGS=()

if [[ -n "${DOCKER_PLATFORM:-}" ]]; then
    PLATFORM_ARGS+=(--platform "$DOCKER_PLATFORM")
fi

docker save "${PLATFORM_ARGS[@]}" "$IMAGE_TAG" -o "$OUTPUT"

echo "Saved $IMAGE_TAG to $OUTPUT"
