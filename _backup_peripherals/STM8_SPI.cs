//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// STM8 SPI peripheral.
// Registers: CR1(0x00), CR2(0x01), ICR(0x02), SR(0x03), DR(0x04).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class STM8_SPI : IBytePeripheral, IKnownSize
    {
        public STM8_SPI(IMachine machine)
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
                case 0x02: return icr;
                case 0x03:
                    byte val = sr;
                    sr &= unchecked((byte)~OVR); // clear OVR on read
                    return val;
                case 0x04:
                    sr &= unchecked((byte)~RXNE);
                    return dr;
            }
            this.Log(LogLevel.Warning, "STM8_SPI: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00: cr1 = value; return;
                case 0x01: cr2 = value; return;
                case 0x02: icr = value; return;
                case 0x03: return; // SR read-only
                case 0x04:
                    dr = value;
                    // In master mode, writing DR starts transfer
                    if((cr1 & SPE) != 0)
                    {
                        sr |= TXE;
                        // Loopback: received byte = sent byte (no slave connected)
                        sr |= RXNE;
                        if((icr & (TXIE | RXIE)) != 0)
                        {
                            IRQ.Set();
                            IRQ.Unset();
                        }
                    }
                    return;
            }
        }

        public void Reset()
        {
            cr1 = 0;
            cr2 = 0;
            icr = 0;
            sr = TXE; // TX empty at reset
            dr = 0;
        }

        public long Size => 0x05;
        public GPIO IRQ { get; }

        private const byte SPE  = 0x40;
        private const byte RXNE = 0x01;
        private const byte TXE  = 0x02;
        private const byte OVR  = 0x40;
        private const byte TXIE = 0x80;
        private const byte RXIE = 0x40;

        private byte cr1, cr2, icr, sr, dr;
    }
}
