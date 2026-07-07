# STM32L476 Zephyr Debug Lab Manual

## Course context

This lab is designed for an embedded systems course or internal engineering training. The student works with a Zephyr-based STM32L476 firmware project and learns hardware debugging and emulation-based analysis in parallel.

## Required hardware

- Nucleo-L476RG or compatible STM32L476 board
- USB cable with data support
- Optional oscilloscope or logic analyzer for PWM
- Optional CAN transceiver and USB-CAN adapter
- Optional CAN bus termination resistors

## Required software

- Zephyr SDK and west
- VS Code
- Cortex-Debug extension
- OpenOCD
- Renode
- Python 3
- Optional Linux CAN tools: `can-utils`

## Project deliverables from the student

Each student should submit:

1. A screenshot of VS Code stopped at `main` on real hardware.
2. A trace log showing GPIO heartbeat and PWM duty changes.
3. A CAN loopback or simulated CAN trace.
4. A Renode log showing at least function tracing or peripheral access logging.
5. A timeline plot generated from `timeline.jsonl`.
6. A short analysis explaining one observed bug or limitation.

## Build and run

```bash
cd app
west build -b nucleo_l476rg -d build
west flash -d build
```

## Debug with VS Code

Open `app/` in VS Code. Select:

```text
STM32L476 OpenOCD Debug
```

Expected behavior:

- The program builds.
- OpenOCD starts.
- The target resets and halts.
- The debugger runs to `main`.

## Debug with Renode

```bash
cd app
renode renode/stm32l476_debug.resc
```

Then attach VS Code with:

```text
Renode GDB Debug STM32L476
```

Note: the minimal Renode platform is for teaching and tracing. It may need extension for exact STM32L476 CAN/PWM behavior.

## Generate a timeline

```bash
cd app
python3 tools/trace_to_timeline.py < renode/run.log > renode/timeline.jsonl
python3 tools/plot_timeline.py < renode/timeline.jsonl
```

## Grading rubric

| Criterion | Points |
|---|---:|
| Builds and flashes Zephyr app | 15 |
| VS Code/OpenOCD debug session works | 20 |
| GPIO/PWM behavior demonstrated | 15 |
| CAN behavior demonstrated or limitation explained | 15 |
| Renode trace/debug run demonstrated | 20 |
| Timeline visualization and written analysis | 15 |
