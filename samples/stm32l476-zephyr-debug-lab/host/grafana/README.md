# Optional dashboard path

For longer tests, push parsed events into a log database such as Loki or a time-series system.
A simple path is:

1. Run firmware or Renode and collect logs.
2. Convert trace logs to JSONL using `app/tools/trace_to_timeline.py`.
3. Ship JSONL lines to your collector.
4. Create panels grouped by `type`: GPIO, PWM, CAN_TX, CAN_RX, IRQ, ERROR.

For small labs, the included matplotlib plot is enough.
