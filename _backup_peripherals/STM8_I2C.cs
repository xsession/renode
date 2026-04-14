//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// STM8 I2C peripheral.
// Registers: CR1(0x00), CR2(0x01), FREQR(0x02), OARL(0x03), OARH(0x04),
//            DR(0x06), SR1(0x07), SR2(0x08), SR3(0x09),
//            CCRL(0x0A), CCRH(0x0B), TRISER(0x0C).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class STM8_I2C : IBytePeripheral, IKnownSize
    {
        public STM8_I2C(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return cr1;
                case 0x01: return cr2;
                case 0x02: return freqr;
                case 0x03: return oarl;
                case 0x04: return oarh;
                case 0x06: return dr;
                case 0x07: return sr1;
                case 0x08: return sr2;
                case 0x09: return sr3;
                case 0x0A: return ccrl;
                case 0x0B: return ccrh;
                case 0x0C: return triser;
            }
            this.Log(LogLevel.Warning, "STM8_I2C: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00: cr1 = value; return;
                case 0x01:
                    cr2 = value;
                    if((cr2 & START) != 0) sr1 |= SB;
                    if((cr2 & STOP) != 0)  sr1 = 0;
                    return;
                case 0x02: freqr = value; return;
                case 0x03: oarl = value; return;
                case 0x04: oarh = value; return;
                case 0x06: dr = value; return;
                case 0x0A: ccrl = value; return;
                case 0x0B: ccrh = value; return;
                case 0x0C: triser = value; return;
            }
        }

        public void Reset()
        {
            cr1 = 0; cr2 = 0; freqr = 0;
            oarl = 0; oarh = 0; dr = 0;
            sr1 = 0; sr2 = 0; sr3 = 0;
            ccrl = 0; ccrh = 0; triser = 0x02;
        }

        public long Size => 0x0D;
        public GPIO IRQ { get; }

        private const byte START = 0x01;
        private const byte STOP  = 0x02;
        private const byte SB    = 0x01;

        private byte cr1, cr2, freqr, oarl, oarh;
        private byte dr, sr1, sr2, sr3;
        private byte ccrl, ccrh, triser;
    }
}
