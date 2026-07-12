# Tools

## Convert trace log to JSONL

```bash
python3 tools/trace_to_timeline.py < sample_trace.log > timeline.jsonl
```

## Plot timeline

```bash
python3 tools/plot_timeline.py < timeline.jsonl
```

## GDB helper commands

Inside GDB:

```gdb
source tools/stack_snapshot.gdb
mark
btfull
```
