//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// STM8 ADC1 (10-bit, single-conversion).
// Registers: CSR(0x00), CR1(0x01), CR2(0x02), CR3(0x03),
//            DRH(0x04), DRL(0x05), TDRH(0x06), TDRL(0x07).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class STM8_ADC : IBytePeripheral, IKnownSize
    {
        public STM8_ADC(IMachine machine, int channelCount = 8)
        {
            this.channelCount = channelCount;
            channelValues = new ushort[channelCount];
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return csr;
                case 0x01: return cr1;
                case 0x02: return cr2;
                case 0x03: return cr3;
                case 0x04: return drh;
                case 0x05: return drl;
                case 0x06: return tdrh;
                case 0x07: return tdrl;
            }
            this.Log(LogLevel.Warning, "STM8_ADC: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00:
                    csr = (byte)((value & 0x0F) | (csr & EOC)); // preserve EOC, CH bits writable
                    if((value & EOC) == 0) csr &= unchecked((byte)~EOC); // w0c
                    return;
                case 0x01:
                    cr1 = value;
                    if((cr1 & ADON) != 0)
                        PerformConversion();
                    return;
                case 0x02: cr2 = value; return;
                case 0x03: cr3 = value; return;
                case 0x06: tdrh = value; return;
                case 0x07: tdrl = value; return;
            }
        }

        public void SetChannelValue(int channel, ushort value)
        {
            if(channel >= 0 && channel < channelCount)
                channelValues[channel] = (ushort)(value & 0x03FF);
        }

        public void Reset()
        {
            csr = 0; cr1 = 0; cr2 = 0; cr3 = 0;
            drh = 0; drl = 0; tdrh = 0; tdrl = 0;
        }

        public long Size => 0x08;
        public GPIO IRQ { get; }

        private void PerformConversion()
        {
            int ch = csr & 0x0F;
            ushort result = (ch < channelCount) ? channelValues[ch] : (ushort)0;

            if((cr2 & ALIGN) != 0)
            {
                // Right-aligned
                drl = (byte)(result & 0xFF);
                drh = (byte)((result >> 8) & 0x03);
            }
            else
            {
                // Left-aligned
                drh = (byte)((result >> 2) & 0xFF);
                drl = (byte)((result & 0x03) << 6);
            }

            csr |= EOC;
            if((csr & EOCIE) != 0)
            {
                IRQ.Set();
                IRQ.Unset();
            }
        }

        private const byte ADON  = 0x01;
        private const byte EOC   = 0x80;
        private const byte EOCIE = 0x20;
        private const byte ALIGN = 0x08;

        private byte csr, cr1, cr2, cr3;
        private byte drh, drl, tdrh, tdrl;
        private readonly int channelCount;
        private readonly ushort[] channelValues;
    }
}
