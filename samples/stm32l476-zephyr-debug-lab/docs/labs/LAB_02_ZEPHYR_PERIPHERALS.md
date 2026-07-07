# Lab 02: Zephyr GPIO, PWM and CAN Peripheral Debug

## Goal

Understand how a Zephyr firmware project accesses board peripherals through devicetree and drivers.

## Tasks

### Task 1: GPIO heartbeat

Set a breakpoint in:

```c
gpio_demo_tick()
```

Expected trace:

```text
GPIO heartbeat led=1 button_irq_count=0
GPIO heartbeat led=0 button_irq_count=0
```

### Task 2: Button interrupt

Set a breakpoint in:

```c
button_pressed()
```

Press the user button. Confirm that `button_irq_count` increments.

### Task 3: PWM duty sweep

Set a breakpoint in:

```c
pwm_demo_tick()
```

Observe `duty_percent` moving through:

```text
10, 20, 30, ..., 90, 10
```

Use an oscilloscope or logic analyzer to confirm the PWM waveform.

### Task 4: CAN loopback or log-only CAN

Set a breakpoint in:

```c
can_demo_tick()
can_rx_callback()
```

If CAN is enabled and loopback works, you should see both TX and RX trace messages.
If your board DTS or transceiver setup is incomplete, explain the limitation and continue with log-only mode.

## Questions

1. Why is devicetree useful for GPIO and PWM portability?
2. Why should CAN be tested first in loopback mode?
3. What information does the trace log give that a register view does not?
4. What information does the register view give that the trace log does not?
