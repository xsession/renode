//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// PIC16 Watchdog Timer.
// Config-word controlled; software can only clear via CLRWDT.
// Register: WDTCON (0x00) — SWDTEN bit.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class PIC16_WDT : IBytePeripheral, IKnownSize
    {
        public PIC16_WDT(IMachine machine, long baseFrequency = 31_250)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, baseFrequency,
                                   this, "PIC16_WDT",
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
            this.Log(LogLevel.Warning, "PIC16_WDT: Read 0x{0:X}", offset);
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
            this.Log(LogLevel.Warning, "PIC16_WDT: Watchdog timeout");
            IRQ.Set();
            IRQ.Unset();
        }

        private const byte SWDTEN = 0x01;
        private byte wdtcon;
        private readonly LimitTimer timer;
    }
}
