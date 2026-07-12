# Lab 04: Visualizing Firmware Runtime Behavior

## Goal

Convert firmware logs into a visual timeline that a human can inspect quickly.

## Procedure

1. Generate or collect a log file.
2. Convert trace events into JSONL:

```bash
cd app
python3 tools/trace_to_timeline.py < renode/run.log > renode/timeline.jsonl
```

3. Plot the timeline:

```bash
python3 tools/plot_timeline.py < renode/timeline.jsonl
```

## What to look for

- GPIO heartbeat every 500 ms
- PWM update every 250 ms
- CAN TX every 1000 ms
- CAN RX shortly after TX when loopback works
- Error bursts or missing periodic events

## Analysis questions

1. Is the PWM update period stable?
2. Does CAN TX happen at the expected period?
3. Are any interrupt events clustered unexpectedly?
4. Is there a missing event after an error message?
5. How would this visualization help during a long overnight test?
