# Lab 01: Hardware Debug with VS Code, Cortex-Debug, OpenOCD and ST-LINK

## Goal

Start a source-level debug session on STM32L476 hardware and stop at `main()`.

## Procedure

1. Connect the Nucleo-L476RG board over USB.
2. Open the `app/` folder in VS Code.
3. Build the application:

```bash
west build -b nucleo_l476rg -d build
```

4. Start the debug configuration:

```text
STM32L476 OpenOCD Debug
```

5. Confirm that execution stops at `main()`.

## Useful GDB commands

```gdb
monitor reset halt
info registers
break main
continue
bt
```

## Observations to record

- OpenOCD target voltage
- Number of breakpoints/watchpoints reported
- Program counter at halt
- Whether source lines are visible

## Troubleshooting

### OpenOCD cannot find the target

Check cable, USB permissions, ST-LINK firmware and whether another program holds the probe.

### Breakpoints do not hit

Use a pristine debug build and confirm the ELF path is:

```text
app/build/zephyr/zephyr.elf
```

### No source lines

Ensure debug info is enabled:

```text
CONFIG_DEBUG=y
CONFIG_DEBUG_INFO=y
CONFIG_NO_OPTIMIZATIONS=y
```
