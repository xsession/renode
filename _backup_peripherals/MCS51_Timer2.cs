//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// MCS-51 Timer 2 (8052-compatible).
// Auto-reload / capture mode. SFR mapped:
//   T2CON(0x00), TH2(0x01), TL2(0x02), RCAP2H(0x03), RCAP2L(0x04).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class MCS51_Timer2 : IBytePeripheral, IKnownSize
    {
        public MCS51_Timer2(IMachine machine, long frequency = 11_059_200)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, frequency / 12,
                                   this, "MCS51_Timer2",
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
                case 0x00: return t2con;
                case 0x01: return (byte)((timer.Value >> 8) & 0xFF);
                case 0x02: return (byte)(timer.Value & 0xFF);
                case 0x03: return rcap2h;
                case 0x04: return rcap2l;
            }
            this.Log(LogLevel.Warning, "MCS51_Timer2: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00:
                    t2con = value;
                    timer.Enabled = (t2con & TR2) != 0;
                    return;
                case 0x01:
                    timer.Value = (uint)((value << 8) | (timer.Value & 0xFF));
                    return;
                case 0x02:
                    timer.Value = (uint)((timer.Value & 0xFF00) | value);
                    return;
                case 0x03: rcap2h = value; return;
                case 0x04: rcap2l = value; return;
            }
        }

        public void Reset()
        {
            t2con = 0;
            rcap2h = 0;
            rcap2l = 0;
            timer.Value = 0;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x05;
        public GPIO IRQ { get; }

        private void OnOverflow()
        {
            t2con |= TF2; // set overflow flag
            // Auto-reload if RCLK=0 and TCLK=0
            if((t2con & (RCLK | TCLK)) == 0)
            {
                timer.Value = (uint)((rcap2h << 8) | rcap2l);
            }
            IRQ.Set();
            IRQ.Unset();
        }

        private const byte TR2  = 0x04;
        private const byte TF2  = 0x80;
        private const byte RCLK = 0x20;
        private const byte TCLK = 0x10;

        private byte t2con;
        private byte rcap2h;
        private byte rcap2l;
        private readonly LimitTimer timer;
    }
}
