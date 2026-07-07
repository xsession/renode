# Teacher Notes

## Suggested schedule

| Week | Topic | Lab |
|---|---|---|
| 1 | Debug architecture | Lab 01 |
| 2 | Zephyr drivers/devicetree | Lab 02 |
| 3 | Emulation and trace | Lab 03 |
| 4 | Visualization and reporting | Lab 04 |

## Common student mistakes

1. Opening the repository root instead of `app/` in VS Code.
2. Forgetting to initialize Zephyr/west before building.
3. Using a charge-only USB cable.
4. Expecting Renode to perfectly emulate every STM32L476 register without adding platform support.
5. Testing CAN normal mode before loopback and before checking bit timing/transceiver wiring.

## Recommended instructor demonstration

1. Build the project.
2. Show `zephyr.elf` in the build directory.
3. Start OpenOCD debug and stop at `main`.
4. Set breakpoints in `gpio_demo_tick`, `pwm_demo_tick`, and `can_demo_tick`.
5. Show live trace output.
6. Start Renode and show the same ELF loaded into the emulator.
7. Generate and plot a timeline.

## Assessment idea

Ask students to introduce a bug, such as changing the CAN period to 100 ms or disabling PWM update. Then ask another student to detect the problem from the timeline and trace logs.
