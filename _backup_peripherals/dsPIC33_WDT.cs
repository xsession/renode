//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// dsPIC33 Watchdog Timer (WDT) module.
//
// The WDT is a free-running timer that resets the device if not cleared.
// Clearing requires writing 0x5743 then 0x4321 to WDTCON<CLRKEY>.
//
// Register (single 16-bit):
//   0x00  WDTCON  — Watchdog Timer Control
//           bit 15   : ON (enable)
//           bits 4:0 : WDTPS (post-scaler select)
//           bit 8    : WDTCLRKEY (write-only sequence)
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class dsPIC33_WDT : IWordPeripheral, IKnownSize
    {
        public dsPIC33_WDT(IMachine machine, long baseFrequency = 32_768)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, baseFrequency,
                                   this, "dsPIC33_WDT",
                                   limit: 1024,
                                   enabled: true,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnLimitReached;
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            if(offset == 0) return wdtcon;
            this.Log(LogLevel.Warning, "dsPIC33_WDT: Read offset 0x{0:X}", offset);
            return 0;
        }

        public void WriteWord(long offset, ushort value)
        {
            if(offset != 0) return;

            wdtcon = (ushort)(value & 0x801F);
            timer.Enabled = (wdtcon & WDTCON_ON) != 0;
            int ps = wdtcon & 0x1F;
            timer.Divider = 1 << ps;

            /* Clear key sequence: 0x01 in bits 8..15 indicates clear attempt */
            if((value & 0xFF00) == 0x0100)
            {
                timer.Value = 0;
            }
        }

        public void Reset()
        {
            wdtcon = WDTCON_ON;
            timer.Value = 0;
            timer.Enabled = true;
            IRQ.Unset();
        }

        public long Size => 0x02;
        public GPIO IRQ { get; }

        private void OnLimitReached()
        {
            this.Log(LogLevel.Warning, "dsPIC33_WDT: Watchdog timeout");
            IRQ.Set();
            IRQ.Unset();
        }

        private const ushort WDTCON_ON = (1 << 15);

        private ushort wdtcon;
        private readonly LimitTimer timer;
    }
}
