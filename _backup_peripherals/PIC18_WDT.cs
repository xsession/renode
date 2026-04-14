//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// PIC18 Watchdog Timer (WDT).
// Software-controllable via SWDTEN bit in WDTCON register.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class PIC18_WDT : IBytePeripheral, IKnownSize
    {
        public PIC18_WDT(IMachine machine, long baseFrequency = 31_250)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, baseFrequency,
                                   this, "PIC18_WDT",
                                   limit: 32768,
                                   enabled: false,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnTimeout;
            Reset();
        }

        public byte ReadByte(long offset)
        {
            if(offset == 0) return wdtcon;
            this.Log(LogLevel.Warning, "PIC18_WDT: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            if(offset == 0)
            {
                wdtcon = (byte)(value & 0x01);
                timer.Enabled = (wdtcon & SWDTEN) != 0;
            }
        }

        public void ClearWatchdog()
        {
            timer.Value = 0;
        }

        public void Reset()
        {
            wdtcon = 0;
            timer.Value = 0;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x01;
        public GPIO IRQ { get; }

        private void OnTimeout()
        {
            this.Log(LogLevel.Warning, "PIC18_WDT: Watchdog timeout — reset");
            IRQ.Set();
            IRQ.Unset();
        }

        private const byte SWDTEN = 0x01;
        private byte wdtcon;
        private readonly LimitTimer timer;
    }
}
