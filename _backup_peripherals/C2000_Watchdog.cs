//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x Watchdog Timer module.
//
// Register map (16-bit word offsets from base):
//   0x00  SCSR    — System Control and Status Register
//   0x02  WDCNTR  — Watchdog counter (read-only, 8-bit)
//   0x04  WDKEY   — Watchdog Key Register (must write 0x55 then 0xAA to reset)
//   0x06  WDCR    — Watchdog Control Register
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class C2000_Watchdog : IWordPeripheral, IKnownSize
    {
        public C2000_Watchdog(IMachine machine, long baseFrequency = 10_000_000)
        {
            IRQ = new GPIO();

            timer = new LimitTimer(machine.ClockSource, baseFrequency / 512,
                                   this, "C2000_WDT",
                                   limit: 256,
                                   enabled: true,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnLimitReached;

            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SCSR:   return scsr;
            case Registers.WDCNTR: return (ushort)(timer.Value & 0xFF);
            case Registers.WDKEY:  return 0;
            case Registers.WDCR:   return wdcr;
            default:
                this.Log(LogLevel.Warning, "C2000_WDT: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.SCSR:
                /* WDOVERRIDE (bit 0), WDENINT (bit 1) */
                scsr = (ushort)(value & 0x0003);
                break;
            case Registers.WDKEY:
                HandleWdKey(value);
                break;
            case Registers.WDCR:
                wdcr = (ushort)(value & 0x007F);
                bool disabled = (wdcr & WDCR_WDDIS) != 0;
                timer.Enabled = !disabled;
                int prescale = 1 << (wdcr & 0x07);
                timer.Divider = prescale;
                break;
            default:
                this.Log(LogLevel.Warning, "C2000_WDT: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            scsr = 0;
            wdcr = 0x0028;
            lastKey = 0;
            timer.Value = 0;
            timer.Enabled = true;
            IRQ.Unset();
        }

        public long Size => 0x08;

        public GPIO IRQ { get; }

        private void HandleWdKey(ushort value)
        {
            if(lastKey == 0x55 && value == 0xAA)
            {
                timer.Value = 0;
            }
            lastKey = value;
        }

        private void OnLimitReached()
        {
            if((scsr & SCSR_WDENINT) != 0)
            {
                IRQ.Set();
                IRQ.Unset();
            }
            else
            {
                this.Log(LogLevel.Warning, "C2000_WDG: Watchdog reset triggered");
            }
        }

        private enum Registers : long
        {
            SCSR   = 0x00,
            WDCNTR = 0x02,
            WDKEY  = 0x04,
            WDCR   = 0x06,
        }

        private const ushort WDCR_WDDIS   = (1 << 6);
        private const ushort SCSR_WDENINT  = (1 << 1);

        private ushort scsr;
        private ushort wdcr;
        private ushort lastKey;
        private readonly LimitTimer timer;
    }
}
