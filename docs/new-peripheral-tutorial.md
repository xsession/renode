# Adding Peripherals to Renode Custom Cores — Tutorial

This guide explains how to add new peripheral models (UART, SPI, I2C, ADC, Timer, GPIO, PWM, DMA, etc.) to the Renode emulator for any CPU architecture.

---

## Table of Contents

1. [Overview](#overview)
2. [Peripheral Types & Interfaces](#peripheral-types--interfaces)
3. [Step-by-Step: Creating a New Peripheral](#step-by-step-creating-a-new-peripheral)
4. [Register Maps](#register-maps)
5. [Interrupt / GPIO Lines](#interrupt--gpio-lines)
6. [Writing the .repl Platform File](#writing-the-repl-platform-file)
7. [Timer Peripherals with LimitTimer](#timer-peripherals-with-limittimer)
8. [Complete Examples](#complete-examples)
9. [Testing Tips](#testing-tips)
10. [Checklist](#checklist)

---

## Overview

Renode models peripherals as C# classes under:

```
src/Infrastructure/src/Emulator/Peripherals/Peripherals/<Category>/
```

Where `<Category>` is one of:
- `UART/` — Serial ports, USARTs
- `SPI/` — SPI master/slave
- `I2C/` — I2C / TWI
- `Timers/` — Timers, PWM, CCP, watchdog
- `GPIOPort/` — Digital I/O ports
- `Analog/` — ADC, DAC
- `Memory/` — EEPROM, Flash
- `DMA/` — DMA controllers

Each class implements one or more bus interfaces and registers with the system bus.

---

## Peripheral Types & Interfaces

### Bus Width Interfaces

| Interface             | Width   | Use For                              |
|-----------------------|---------|--------------------------------------|
| `IBytePeripheral`     | 8-bit   | AVR, STM8, PIC16, PIC18, 8051       |
| `IWordPeripheral`     | 16-bit  | dsPIC33, C2000 (some peripherals)    |
| `IDoubleWordPeripheral` | 32-bit | ARM, RISC-V, C2000 (GPIO)          |

### Common Interfaces

| Interface              | Purpose                                    |
|------------------------|--------------------------------------------|
| `IKnownSize`           | Required — declares `long Size`            |
| `IUART`                | For UART peripherals (CharReceived, etc.)  |
| `IGPIOReceiver`        | Peripheral can receive GPIO signals        |
| `INumberedGPIOOutput`  | Peripheral drives numbered output pins     |

### Choosing the Right Interface

```
8-bit architecture (AVR, PIC, 8051, STM8)?
  → IBytePeripheral : ReadByte(long offset) / WriteByte(long offset, byte value)

16-bit architecture (dsPIC33)?
  → IWordPeripheral : ReadWord(long offset) / WriteWord(long offset, ushort value)

32-bit architecture (ARM, RISC-V)?
  → IDoubleWordPeripheral : ReadDoubleWord(long offset) / WriteDoubleWord(long offset, uint value)
```

---

## Step-by-Step: Creating a New Peripheral

### Step 1: Create the C# File

Create a new `.cs` file in the appropriate category folder:

```
src/Infrastructure/src/Emulator/Peripherals/Peripherals/<Category>/<Arch>_<Peripheral>.cs
```

Naming convention: `<Architecture>_<PeripheralType>.cs`

Examples:
- `AVR_SPI.cs`
- `PIC16_MSSP_SPI.cs`
- `dsPIC33_ADC.cs`
- `MCS51_UART.cs`

### Step 2: Basic Structure

```csharp
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.<Category>
{
    public class <Arch>_<Peripheral> : I<Width>Peripheral, IKnownSize
    {
        public <Arch>_<Peripheral>(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        // Bus read method
        public <type> Read<Width>(long offset)
        {
            switch((<RegisterEnum>)offset)
            {
            case <RegisterEnum>.REG_A: return regA;
            // ... more registers
            default:
                this.Log(LogLevel.Warning, "Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        // Bus write method
        public void Write<Width>(long offset, <type> value)
        {
            switch((<RegisterEnum>)offset)
            {
            case <RegisterEnum>.REG_A:
                regA = value;
                break;
            // ... more registers
            default:
                this.Log(LogLevel.Warning, "Write to unknown offset 0x{0:X}", offset);
                break;
            }
        }

        public void Reset()
        {
            regA = 0;
            IRQ.Unset();
        }

        public long Size => 0x04;  // Total register space in bytes
        public GPIO IRQ { get; }

        private enum <RegisterEnum> : long
        {
            REG_A = 0x00,
            REG_B = 0x01,
            // Offsets from peripheral base address
        }

        private <type> regA;
    }
}
```

### Step 3: Implement the Logic

For each register write, implement the hardware behavior:
- Check enable bits before acting
- Set status flags after operations
- Fire interrupts when conditions are met
- Masking: always mask values to the correct bit width

### Step 4: Wire into the .repl File

Add the peripheral to the platform description. See [Writing the .repl Platform File](#writing-the-repl-platform-file).

---

## Register Maps

Define registers as an enum with byte offsets from the peripheral base:

```csharp
private enum Registers : long
{
    CONTROL  = 0x00,
    STATUS   = 0x01,
    DATA_LOW = 0x02,
    DATA_HIGH = 0x03,
}
```

**Important:** The offset values must match the hardware datasheet, relative to the peripheral's base address in the .repl file.

### Special Register Behaviors

| Pattern | Implementation |
|---------|---------------|
| Write-1-to-clear | `if((value & BIT) != 0) reg &= ~BIT;` |
| Read-only bits | `reg = (reg & RO_MASK) \| (value & ~RO_MASK);` |
| Read-clears-flag | Clear the flag in the ReadByte method after returning |
| Side-effect on read | Perform action in ReadByte, e.g., dequeue RX data |
| Double-buffered | Use a temp variable (like AVR's 16-bit timer temp byte) |

---

## Interrupt / GPIO Lines

### Declaring IRQ Outputs

```csharp
// In the constructor:
IRQ = new GPIO();

// As a property:
public GPIO IRQ { get; }
```

### Firing Edge-Triggered Interrupts

Most embedded IRQs are edge-triggered in Renode:

```csharp
IRQ.Set();    // Assert
IRQ.Unset();  // De-assert (creates a pulse)
```

### Multiple IRQ Lines

For peripherals with multiple interrupt sources:

```csharp
public GPIO IRQ_TX { get; }
public GPIO IRQ_RX { get; }
public GPIO IRQ_OVF { get; }
```

### Connecting in .repl

```
uart: UART.AVR_USART @ sysbus <0x8000C0, +0x07>
    -> cpu@0       // Single IRQ connected to CPU interrupt 0
```

For multiple connections:
```
timer: Timers.AVR_Timer16 @ sysbus <0x800080, +0x0C>
    IRQ_OVF -> cpu@13
    IRQ_COMPA -> cpu@14
```

---

## Writing the .repl Platform File

Platform files are at `platforms/cpus/<chip>.repl`.

### Basic Syntax

```
<instance_name>: <Category>.<ClassName> @ sysbus <base_address, +size>
    <constructor_param>: <value>
    -> cpu@<irq_number>
```

### Address Mapping Conventions

| Architecture | Data Space Formula | Example |
|--------------|-------------------|---------|
| AVR | `0x800000 + io_addr` | USART at 0xC0 → `0x8000C0` |
| PIC16 | `0x00 + sfr_addr` | SSPCON at 0x14 → `0x000014` |
| PIC18 | `0x0F00 + sfr_offset` | SSPCON1 at 0xFC6 → `0x000FC6` |
| dsPIC33 | Direct SFR address | UART1 at 0x0220 → `0x000220` |
| 8051 (MCS-51) | `0x300000 + sfr_addr` | SCON at 0x98 → `0x300098` |
| STM8 | Direct I/O address | UART at 0x5230 → `0x005230` |

### Complete .repl Example (AT89S52)

```
cpu: CPU.MCS51 @ sysbus
    cpuType: "at89s52"

flash: Memory.MappedMemory @ sysbus 0x000000
    size: 0x002000

iram: Memory.MappedMemory @ sysbus 0x100000
    size: 0x000100

xdata: Memory.MappedMemory @ sysbus 0x200000
    size: 0x010000

sfr: Memory.MappedMemory @ sysbus 0x300000
    size: 0x000100

uart: UART.MCS51_UART @ sysbus <0x300098, +0x03>
    baseFrequency: 11059200
    -> cpu@0

timer: Timers.MCS51_Timer @ sysbus <0x300088, +0x06>
    baseFrequency: 11059200
    -> cpu@0

port0: GPIOPort.MCS51_GPIO @ sysbus <0x300080, +0x01>
port1: GPIOPort.MCS51_GPIO @ sysbus <0x300090, +0x01>
```

---

## Timer Peripherals with LimitTimer

Renode provides `LimitTimer` for modeling hardware counters:

```csharp
using Antmicro.Renode.Time;

// In constructor:
innerTimer = new LimitTimer(
    machine.ClockSource,   // Clock provider
    baseFrequency,         // Hz (e.g., 16_000_000 for 16 MHz)
    this,                  // Owner (for logging)
    "Timer_Name",          // Display name
    limit: 0xFFFF,         // Count limit (overflow point)
    enabled: false,        // Start disabled
    eventEnabled: true     // Fire LimitReached events
);

innerTimer.LimitReached += OnOverflow;
```

### Key Properties

| Property | Description |
|----------|-------------|
| `Enabled` | Start/stop the timer |
| `Value` | Current count (read/write) |
| `Limit` | Maximum count before overflow |
| `Divider` | Clock prescaler value |

### Prescaler Pattern

```csharp
private void ApplyClockSelect()
{
    int cs = controlReg & 0x07;
    switch(cs)
    {
    case 0: innerTimer.Enabled = false; break;
    case 1: innerTimer.Divider = 1;    innerTimer.Enabled = true; break;
    case 2: innerTimer.Divider = 8;    innerTimer.Enabled = true; break;
    case 3: innerTimer.Divider = 64;   innerTimer.Enabled = true; break;
    case 4: innerTimer.Divider = 256;  innerTimer.Enabled = true; break;
    case 5: innerTimer.Divider = 1024; innerTimer.Enabled = true; break;
    }
}
```

---

## Complete Examples

### Example 1: UART (8-bit, IBytePeripheral + IUART)

Key pattern for UART peripherals:

```csharp
public class MyArch_UART : IUART, IBytePeripheral, IKnownSize
{
    // IUART requires:
    public event Action<byte> CharReceived;   // Fired when TX writes
    public void WriteChar(byte value);        // Called when external data arrives
    public uint BaudRate { get; }
    public Bits StopBits { get; }
    public Parity ParityBit { get; }

    // TX path: WriteByte(DATA_REG, value) → CharReceived?.Invoke(value)
    // RX path: WriteChar(value) → enqueue → ReadByte(DATA_REG) dequeues
}
```

### Example 2: GPIO Port (8-bit, with bidirectional I/O)

```csharp
public class MyArch_GPIO : IBytePeripheral, IGPIOReceiver, IKnownSize, INumberedGPIOOutput
{
    // Create 8 output GPIO connections:
    Connections = new Dictionary<int, IGPIO>();
    for(int i = 0; i < 8; i++) Connections[i] = new GPIO();

    // Drive outputs when port register written:
    private void DriveOutputs()
    {
        for(int i = 0; i < 8; i++)
            if((ddr & (1 << i)) != 0)           // If pin configured as output
                Connections[i].Set((port & (1 << i)) != 0);
    }

    // Receive external input:
    public void OnGPIO(int number, bool value) { ... }
}
```

### Example 3: ADC (analog input)

```csharp
public class MyArch_ADC : IBytePeripheral, IKnownSize
{
    // External API to set channel voltages:
    public void SetChannelValue(int channel, ushort value) { ... }

    // Conversion triggered by writing GO/DONE bit:
    private void PerformConversion()
    {
        int ch = GetSelectedChannel();
        adcResult = channelValues[ch];
        ClearGoBit();
        FireIRQ();
    }
}
```

### Example 4: SPI (basic master)

```csharp
public class MyArch_SPI : IBytePeripheral, IKnownSize
{
    // Write to SPDR triggers full-duplex transfer:
    private void DoTransfer()
    {
        rxData = 0xFF;                    // Or exchange with registered slave
        statusReg |= STATUS_TRANSFER_COMPLETE;
        FireIRQ();
        TransferCompleted?.Invoke(txData, rxData);
    }
}
```

---

## Testing Tips

1. **Monitor register reads/writes** — Use `this.Log(LogLevel.Debug, ...)` for tracing
2. **Test in Renode console**:
   ```
   (monitor) mach create
   (monitor) machine LoadPlatformDescription @platforms/cpus/atmega328p.repl
   (monitor) sysbus ReadByte 0x8000C0   # Read UART register
   (monitor) sysbus WriteByte 0x8000C0 0x42   # Write to UART
   ```
3. **Check peripheral registration**: `peripherals` command lists all registered peripherals
4. **Use `logLevel` to increase verbosity**: `logLevel 0 uart` for Debug level

---

## Checklist

When adding a new peripheral, verify:

- [ ] **Namespace** matches the category folder (`Antmicro.Renode.Peripherals.<Category>`)
- [ ] **Bus interface** matches architecture width (`IBytePeripheral` / `IWordPeripheral` / etc.)
- [ ] **`IKnownSize`** implemented with correct `Size` (must cover all register offsets)
- [ ] **`Reset()`** method initializes all registers to power-on values
- [ ] **Constructor** takes `IMachine machine` as first parameter
- [ ] **Register offsets** match the hardware datasheet
- [ ] **IRQ GPIO** declared and pulsed (Set/Unset) when interrupt conditions met
- [ ] **Unknown register offsets** logged with `LogLevel.Warning`
- [ ] **.repl file** updated with correct base address and size
- [ ] **IRQ connections** wired in .repl (`-> cpu@N` or named `IRQ_NAME -> cpu@N`)

---

## Peripheral Inventory

### Current Peripherals by Architecture

| Architecture | UART | Timer | GPIO | SPI | I2C | ADC | Other |
|-------------|------|-------|------|-----|-----|-----|-------|
| AVR | AVR_USART | AVR_Timer8, AVR_Timer16 | AVR_GPIO | AVR_SPI | AVR_TWI | AVR_ADC | AVR_WDT |
| STM8 | STM8_UART | STM8_Timer | STM8_GPIO | — | — | — | — |
| PIC16 | PIC16_USART | PIC16_Timer0, PIC16_CCP | PIC16_GPIO | PIC16_MSSP_SPI | PIC16_MSSP_I2C | PIC16_ADC | PIC16_EEPROM |
| PIC18 | PIC18_EUSART | PIC18_Timer0, PIC18_CCP, PIC18_PWM | PIC18_GPIO | PIC18_MSSP_SPI | PIC18_MSSP_I2C | PIC18_ADC | — |
| dsPIC33 | dsPIC33_UART | dsPIC33_Timer, dsPIC33_PWM | dsPIC33_GPIO | dsPIC33_SPI | dsPIC33_I2C | dsPIC33_ADC | dsPIC33_DMA |
| C2000 | C2000_SCI | C2000_CPUTimer | C2000_GPIO | — | — | — | — |
| MCS-51 | MCS51_UART | MCS51_Timer | MCS51_GPIO | — | — | — | — |
