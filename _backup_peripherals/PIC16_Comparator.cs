//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// PIC16 Analog Comparator module (CMCON).
// Two comparators with mux'd inputs; CMCON register at offset 0x00.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class PIC16_Comparator : IBytePeripheral, IKnownSize
    {
        public PIC16_Comparator(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return cmcon;
                case 0x01: return cvrcon;
            }
            this.Log(LogLevel.Warning, "PIC16_Comparator: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00:
                    cmcon = value;
                    return;
                case 0x01:
                    cvrcon = value;
                    return;
            }
        }

        public void Reset()
        {
            cmcon = 0x07; // comparators off by default (CM2:CM0 = 111)
            cvrcon = 0;
        }

        public long Size => 0x02;
        public GPIO IRQ { get; }

        private byte cmcon;
        private byte cvrcon;
    }
}
