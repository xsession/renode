# STM32L476 Zephyr Debug Lab

A university-style embedded firmware laboratory project for **STM32L476 / Nucleo-L476RG** using:

- Zephyr RTOS firmware
- GPIO heartbeat and button interrupt
- PWM signal generation
- CAN TX/RX path with loopback-friendly design
- VS Code + Cortex-Debug + OpenOCD hardware debug
- Renode-based emulation/debug workflow
- Trace logging, call-flow analysis, event timeline visualization
- Lab manuals and lecture notes

> Target board: `nucleo_l476rg`.
>
> The project is intentionally structured as a teaching lab. Some Renode STM32L476 peripheral models may require extension depending on your Renode version and how exact you want the CAN/TIM/GPIO emulation to be.

## Quick start

```bash
cd app
west build -b nucleo_l476rg -d build
west flash
west debug
```

Or open the repository in VS Code and use:

- **Build Zephyr app** task
- **STM32L476 OpenOCD Debug** launch configuration
- **Renode GDB Debug STM32L476** launch configuration

## Repository map

```text
app/                 Zephyr application
app/src/             Firmware source
app/.vscode/         VS Code tasks and debug launch configs
app/renode/          Renode platform and run scripts
app/tools/           Trace parsing and visualization tools
docs/                University-style lab and lecture material
tests/renode/        Renode/Robot-style test skeletons
host/can/            Linux SocketCAN helper scripts
host/grafana/        Notes for optional dashboarding
```

## Main documentation

Start here:

1. `docs/LECTURE.md`
2. `docs/LAB_MANUAL.md`
3. `docs/labs/LAB_01_HARDWARE_DEBUG.md`
4. `docs/labs/LAB_02_ZEPHYR_PERIPHERALS.md`
5. `docs/labs/LAB_03_RENODE_TRACE.md`
6. `docs/labs/LAB_04_SYSTEM_VISUALIZATION.md`

## Current known limitations

- Hardware OpenOCD debugging is expected to work on Nucleo-L476RG with ST-LINK.
- Zephyr CAN device naming can depend on board/device-tree support and enabled drivers.
- Renode support for exact STM32L476 CAN/PWM behavior may require a more complete `.repl` platform or custom peripheral model.
- The Renode scripts are designed as a starting point for trace-driven firmware debugging, not as a guaranteed bit-accurate STM32L476 simulator.

