#!/usr/bin/env python3
"""Plot JSONL timeline events produced by trace_to_timeline.py."""

import json
import sys
import matplotlib.pyplot as plt

Y = {
    "boot": 0,
    "gpio": 1,
    "pwm": 2,
    "can_tx": 3,
    "can_rx": 4,
    "irq": 5,
    "error": 6,
    "event": 7,
}

xs = []
ys = []
labels = []

for line in sys.stdin:
    if not line.strip():
        continue
    event = json.loads(line)
    xs.append(event["time_ms"] / 1000.0)
    ys.append(Y.get(event["type"], Y["event"]))
    labels.append(event["message"])

plt.figure(figsize=(12, 6))
plt.scatter(xs, ys)
plt.yticks(list(Y.values()), list(Y.keys()))
plt.xlabel("time [s]")
plt.ylabel("event type")
plt.title("STM32L476 Zephyr Debug Lab timeline")
plt.grid(True)
plt.tight_layout()
plt.show()
