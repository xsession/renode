//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// PIC16 Timer2 — 8-bit timer with period register.
// Registers: T2CON (0x00), TMR2 (0x01), PR2 (0x02).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class PIC16_Timer2 : IBytePeripheral, IKnownSize
    {
        public PIC16_Timer2(IMachine machine, long frequency = 4_000_000)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, frequency,
                                   this, "PIC16_Timer2",
                                   limit: 0xFF,
                                   enabled: false,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnMatch;
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return t2con;
                case 0x01: return (byte)timer.Value;
                case 0x02: return pr2;
            }
            this.Log(LogLevel.Warning, "PIC16_Timer2: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00:
                    t2con = value;
                    timer.Enabled = (t2con & TMR2ON) != 0;
                    int pre = prescalerTable[t2con & 0x03];
                    timer.Divider = pre;
                    postscaler = ((t2con >> 3) & 0x0F) + 1;
                    return;
                case 0x01:
                    timer.Value = value;
                    return;
                case 0x02:
                    pr2 = value;
                    timer.Limit = pr2;
                    return;
            }
        }

        public void Reset()
        {
            t2con = 0;
            pr2 = 0xFF;
            postscaler = 1;
            postCount = 0;
            timer.Value = 0;
            timer.Limit = 0xFF;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x03;
        public GPIO IRQ { get; }

        private void OnMatch()
        {
            postCount++;
            if(postCount >= postscaler)
            {
                postCount = 0;
                IRQ.Set();
                IRQ.Unset();
            }
        }

        private const byte TMR2ON = 0x04;
        private static readonly int[] prescalerTable = { 1, 4, 16, 16 };
        private byte t2con;
        private byte pr2;
        private int postscaler;
        private int postCount;
        private readonly LimitTimer timer;
    }
}
