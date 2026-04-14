//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// dsPIC30F Watchdog Timer (WDT).
// Simple post-scaler watchdog; cleared by CLRWDT instruction (externally).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class dsPIC30_WDT : IWordPeripheral, IKnownSize
    {
        public dsPIC30_WDT(IMachine machine, long baseFrequency = 32_768)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, baseFrequency,
                                   this, "dsPIC30_WDT",
                                   limit: 1024,
                                   enabled: true,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnLimitReached;
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            if(offset == 0) return rcon;
            this.Log(LogLevel.Warning, "dsPIC30_WDT: Read offset 0x{0:X}", offset);
            return 0;
        }

        public void WriteWord(long offset, ushort value)
        {
            if(offset == 0)
            {
                rcon = value;
                /* SWDTEN bit 5 */
                timer.Enabled = (rcon & (1 << 5)) != 0;
            }
        }

        public void ClearWatchdog()
        {
            timer.Value = 0;
        }

        public void Reset()
        {
            rcon = 0;
            timer.Value = 0;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x02;
        public GPIO IRQ { get; }

        private void OnLimitReached()
        {
            this.Log(LogLevel.Warning, "dsPIC30_WDT: Watchdog timeout");
            IRQ.Set();
            IRQ.Unset();
        }

        private ushort rcon;
        private readonly LimitTimer timer;
    }
}
