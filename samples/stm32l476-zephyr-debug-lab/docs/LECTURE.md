# Lecture: Trace-Driven Embedded Debugging with STM32L476, Zephyr, OpenOCD, and Renode

## Learning objectives

After this lecture/lab sequence, the student should be able to:

1. Build a Zephyr application for `nucleo_l476rg`.
2. Connect VS Code Cortex-Debug to OpenOCD over ST-LINK.
3. Debug GPIO, PWM, CAN and interrupt paths using breakpoints and register views.
4. Run the same ELF in Renode where supported.
5. Use function traces, peripheral access logs and firmware trace events to explain system behavior.
6. Convert logs into a timeline visualization for human monitoring.

## Why two debug worlds?

Real hardware and emulation answer different questions.

| Debug world | Best for | Weakness |
|---|---|---|
| Real STM32L476 hardware | Electrical behavior, exact clocks, pin mux, CAN transceiver, PWM waveform | Harder to reproduce rare timing/state bugs |
| Renode | Repeatable execution, tracing, call-flow inspection, automated tests | Peripheral model may be incomplete or not bit-accurate |

A professional workflow uses both. Hardware validates reality. Emulation validates repeatable software behavior.

## Debug observability layers

### Layer 1: Firmware semantic trace

The application prints events such as:

```text
[0000001000 ms] CAN      TX id=0x123 duty=40 button=0 led=1
[0000001250 ms] PWM      duty=50%
[0000001500 ms] GPIO     heartbeat led=0 button_irq_count=0
```

This is the most useful layer for a human operator.

### Layer 2: Debugger state

GDB/VS Code exposes:

- source-level stepping
- local variables
- global state
- CPU registers
- stack traces
- peripheral SVD registers

### Layer 3: Renode execution and peripheral logs

Renode can log function names and peripheral bus accesses. This helps answer:

- Which function path did the firmware execute?
- Which register block did it access?
- Did a driver write the expected peripheral register?

## Firmware design pattern

The lab code uses small modules:

| Module | Responsibility |
|---|---|
| `main.c` | scheduling of demo ticks |
| `gpio_demo.c` | LED heartbeat and button interrupt |
| `pwm_demo.c` | PWM duty sweep |
| `can_demo.c` | CAN TX/RX demo |
| `app_trace.c` | structured trace messages |
| `fault_injection.c` | GDB-callable hooks |

This separation makes it easier to set breakpoints and compare hardware with Renode.

## Key takeaway

A good embedded debug lab is not only “can I flash firmware?”. It is:

1. Can I explain the runtime behavior?
2. Can I reproduce a bug?
3. Can I see the interrupt path?
4. Can I compare real hardware behavior to simulated behavior?
5. Can I make the result visible enough for another engineer to understand?
