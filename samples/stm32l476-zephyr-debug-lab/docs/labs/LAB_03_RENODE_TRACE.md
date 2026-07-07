# Lab 03: Renode Debugging, Call Flow and Peripheral Access Trace

## Goal

Run the Zephyr ELF in Renode and collect execution/peripheral traces.

## Procedure

1. Build the app:

```bash
cd app
west build -b nucleo_l476rg -d build
```

2. Start Renode:

```bash
renode renode/stm32l476_debug.resc
```

3. Attach VS Code using:

```text
Renode GDB Debug STM32L476
```

4. Confirm that GDB connects to port `3333`.

## Trace commands

The Renode script already enables:

```text
cpu LogFunctionNames true
sysbus LogPeripheralAccess gpioA
sysbus LogPeripheralAccess timer2
sysbus LogPeripheralAccess can1_stub
```

## Expected result

The file below should be generated:

```text
app/renode/run.log
```

The content should include boot messages and possibly function/peripheral logs.

## Important limitation

The included `.repl` is intentionally minimal. If Zephyr accesses an unmapped or insufficiently modeled STM32L476 register, Renode may stop or behave differently from real hardware. This is not failure of the lab; it is the point where students learn how emulation models are built and extended.

## Exercise

Find the first unsupported or suspicious peripheral access in `run.log`. Answer:

1. Which address was accessed?
2. Which driver probably accessed it?
3. Is it RCC, GPIO, TIM, CAN, EXTI, UART or another block?
4. Should it be stubbed, modeled, or avoided in simulation?
