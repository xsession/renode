# Zephyr firmware application

This application demonstrates:

- GPIO output heartbeat
- GPIO input interrupt from a button
- PWM duty-cycle sweep
- CAN TX/RX demo path
- structured trace events for hardware and Renode debugging

## Build

```bash
west build -b nucleo_l476rg -d build
```

## Flash

```bash
west flash -d build
```

## Debug

```bash
west debug -d build
```

Or use VS Code launch configurations in `.vscode/launch.json`.
