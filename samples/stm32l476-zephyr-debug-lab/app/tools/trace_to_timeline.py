#!/usr/bin/env python3
"""Convert firmware/Renode logs into JSONL timeline events."""

import json
import re
import sys

TRACE_RE = re.compile(r"\[(\d+)\s+ms\]\s+([A-Za-z0-9_\-]+)\s+(.*)")


def classify(module: str, message: str) -> str:
    text = f"{module} {message}".upper()
    if "CAN" in text and "RX" in text:
        return "can_rx"
    if "CAN" in text and "TX" in text:
        return "can_tx"
    if "PWM" in text:
        return "pwm"
    if "GPIO" in text:
        return "gpio"
    if "IRQ" in text:
        return "irq"
    if "ERR" in text or "FAIL" in text:
        return "error"
    if "BOOT" in text:
        return "boot"
    return "event"


def main() -> int:
    for line in sys.stdin:
        match = TRACE_RE.search(line)
        if not match:
            continue
        time_ms = int(match.group(1))
        module = match.group(2)
        message = match.group(3).strip()
        event = {
            "time_ms": time_ms,
            "module": module,
            "type": classify(module, message),
            "message": message,
        }
        print(json.dumps(event))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
