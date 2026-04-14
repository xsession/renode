//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// PIC18 Timer1 — 16-bit timer/counter.
// Registers: T1CON (0x00), TMR1L (0x01), TMR1H (0x02).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class PIC18_Timer1 : IBytePeripheral, IKnownSize
    {
        public PIC18_Timer1(IMachine machine, long frequency = 10_000_000)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, frequency,
                                   this, "PIC18_Timer1",
                                   limit: 0x10000,
                                   enabled: false,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnOverflow;
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return t1con;
                case 0x01: return (byte)(timer.Value & 0xFF);
                case 0x02: return (byte)((timer.Value >> 8) & 0xFF);
            }
            this.Log(LogLevel.Warning, "PIC18_Timer1: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00:
                    t1con = value;
                    timer.Enabled = (t1con & TMR1ON) != 0;
                    int prescale = 1 << ((t1con >> 4) & 0x03); // T1CKPS
                    timer.Divider = prescale;
                    return;
                case 0x01:
                    tmr1Latch = value;
                    return;
                case 0x02:
                    timer.Value = (uint)((value << 8) | tmr1Latch);
                    return;
            }
        }

        public void Reset()
        {
            t1con = 0;
            tmr1Latch = 0;
            timer.Value = 0;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x03;
        public GPIO IRQ { get; }

        private void OnOverflow()
        {
            IRQ.Set();
            IRQ.Unset();
        }

        private const byte TMR1ON = 0x01;
        private byte t1con;
        private byte tmr1Latch;
        private readonly LimitTimer timer;
    }
}
