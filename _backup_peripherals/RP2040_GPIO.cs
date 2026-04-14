//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 GPIO — 30 GPIO pins with IO Bank 0 controls.
//
// IO_BANK0 register map (32-bit, byte offsets):
//   Per GPIO (stride=8, 30 GPIOs → 0x000–0x0EF):
//     0x000+(n*8)+0  GPIO_STATUS  — GPIO status (read-only)
//     0x000+(n*8)+4  GPIO_CTRL    — GPIO control (FUNCSEL, OEOVER, OUTOVER, etc.)
//
//   Global:
//     0x0F0  INTR0      — Raw interrupts (GPIO 0-7)
//     0x0F4  INTR1      — Raw interrupts (GPIO 8-15)
//     0x0F8  INTR2      — Raw interrupts (GPIO 16-23)
//     0x0FC  INTR3      — Raw interrupts (GPIO 24-29)
//
// SIO register map (at separate address, typically 0xD0000000):
//   0x004  GPIO_IN        — Input values
//   0x010  GPIO_OUT       — Output values
//   0x014  GPIO_OUT_SET
//   0x018  GPIO_OUT_CLR
//   0x01C  GPIO_OUT_XOR
//   0x020  GPIO_OE        — Output enable
//   0x024  GPIO_OE_SET
//   0x028  GPIO_OE_CLR
//   0x02C  GPIO_OE_XOR
//
// This model covers the SIO-side GPIO data/direction registers.
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    public class RP2040_GPIO : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_GPIO(IMachine machine, int numberOfPins = 30)
        {
            this.numberOfPins = numberOfPins;
            pinMask = (uint)((1L << numberOfPins) - 1);
            Connections = new GPIO[numberOfPins];
            for(int i = 0; i < numberOfPins; i++)
                Connections[i] = new GPIO();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.GPIO_IN:      return inputLatch & pinMask;
            case Registers.GPIO_OUT:     return outValue;
            case Registers.GPIO_OUT_SET: return outValue;
            case Registers.GPIO_OUT_CLR: return outValue;
            case Registers.GPIO_OUT_XOR: return outValue;
            case Registers.GPIO_OE:      return oeValue;
            case Registers.GPIO_OE_SET:  return oeValue;
            case Registers.GPIO_OE_CLR:  return oeValue;
            case Registers.GPIO_OE_XOR:  return oeValue;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_GPIO: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.GPIO_OUT:
                outValue = value & pinMask;
                SyncOutputs();
                break;
            case Registers.GPIO_OUT_SET:
                outValue |= value & pinMask;
                SyncOutputs();
                break;
            case Registers.GPIO_OUT_CLR:
                outValue &= ~(value & pinMask);
                SyncOutputs();
                break;
            case Registers.GPIO_OUT_XOR:
                outValue ^= value & pinMask;
                SyncOutputs();
                break;
            case Registers.GPIO_OE:
                oeValue = value & pinMask;
                SyncOutputs();
                break;
            case Registers.GPIO_OE_SET:
                oeValue |= value & pinMask;
                SyncOutputs();
                break;
            case Registers.GPIO_OE_CLR:
                oeValue &= ~(value & pinMask);
                SyncOutputs();
                break;
            case Registers.GPIO_OE_XOR:
                oeValue ^= value & pinMask;
                SyncOutputs();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_GPIO: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            outValue   = 0;
            oeValue    = 0;
            inputLatch = 0;
            for(int i = 0; i < numberOfPins; i++)
                Connections[i].Unset();
        }

        /// <summary>Set an external input on a specific pin (0-based).</summary>
        public void SetInput(int pin, bool state)
        {
            if(pin < 0 || pin >= numberOfPins) return;
            if(state)
                inputLatch |= (1u << pin);
            else
                inputLatch &= ~(1u << pin);
        }

        public long Size => 0x1000;
        public GPIO[] Connections { get; }

        private void SyncOutputs()
        {
            for(int i = 0; i < numberOfPins; i++)
            {
                if((oeValue & (1u << i)) != 0)
                {
                    bool high = (outValue & (1u << i)) != 0;
                    if(high)
                        Connections[i].Set();
                    else
                        Connections[i].Unset();
                }
            }
        }

        private enum Registers : long
        {
            GPIO_IN      = 0x004,
            GPIO_OUT     = 0x010,
            GPIO_OUT_SET = 0x014,
            GPIO_OUT_CLR = 0x018,
            GPIO_OUT_XOR = 0x01C,
            GPIO_OE      = 0x020,
            GPIO_OE_SET  = 0x024,
            GPIO_OE_CLR  = 0x028,
            GPIO_OE_XOR  = 0x02C,
        }

        private readonly int numberOfPins;
        private readonly uint pinMask;
        private uint outValue, oeValue, inputLatch;
    }
}
