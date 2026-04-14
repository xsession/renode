//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// MCS-51 Interrupt Enable / Priority registers.
// IE (0x00) — individual enable bits for INT0/1, Timer0/1/2, Serial.
// IP (0x01) — corresponding priority bits.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class MCS51_IE : IBytePeripheral, IKnownSize
    {
        public MCS51_IE(IMachine machine)
        {
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return ie;
                case 0x01: return ip;
            }
            this.Log(LogLevel.Warning, "MCS51_IE: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00: ie = value; return;
                case 0x01: ip = value; return;
            }
        }

        public bool IsEnabled(int source)
        {
            if((ie & EA) == 0) return false;
            return (ie & (1 << source)) != 0;
        }

        public bool IsHighPriority(int source)
        {
            return (ip & (1 << source)) != 0;
        }

        public void Reset()
        {
            ie = 0;
            ip = 0;
        }

        public long Size => 0x02;

        // IE bit positions
        // 0=EX0, 1=ET0, 2=EX1, 3=ET1, 4=ES, 5=ET2, 7=EA
        private const byte EA = 0x80;

        private byte ie;
        private byte ip;
    }
}
