#!/usr/bin/env bash
set -euo pipefail

CAN_IFACE="${1:-vcan0}"

printf 'Start a listener in another terminal:\n'
printf '  candump %s\n\n' "$CAN_IFACE"

for i in $(seq 0 9); do
    cansend "$CAN_IFACE" "123#A5$(printf '%02X' "$i")112233445566"
    sleep 0.5
done
