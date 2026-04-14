//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// PIC18 Data EEPROM module.
// Registers: EECON1, EECON2 (virtual), EEDATA, EEADR.
// Write requires unlock sequence: write 0x55 then 0xAA to EECON2, then set WR.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Memory
{
    public class PIC18_EEPROM : IBytePeripheral, IKnownSize
    {
        public PIC18_EEPROM(IMachine machine, int eepromSize = 256)
        {
            this.eepromSize = eepromSize;
            storage = new byte[eepromSize];
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return eecon1;
                case 0x01: return 0; // EECON2 always reads 0
                case 0x02: return eedata;
                case 0x03: return eeadr;
            }
            this.Log(LogLevel.Warning, "PIC18_EEPROM: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00: // EECON1
                    eecon1 = (byte)(value & 0x9F); // mask reserved bits
                    if((eecon1 & RD) != 0)
                    {
                        // Read operation
                        if(eeadr < eepromSize)
                            eedata = storage[eeadr];
                        eecon1 &= unchecked((byte)~RD); // auto-clear RD
                    }
                    if((eecon1 & WR) != 0 && unlockState == 2)
                    {
                        // Write operation (WREN must be set)
                        if((eecon1 & WREN) != 0 && eeadr < eepromSize)
                        {
                            storage[eeadr] = eedata;
                        }
                        eecon1 &= unchecked((byte)~WR); // auto-clear WR
                        eecon1 |= EEIF; // set interrupt flag
                        IRQ.Set();
                        IRQ.Unset();
                    }
                    unlockState = 0;
                    return;

                case 0x01: // EECON2 (unlock sequence)
                    if(value == 0x55 && unlockState == 0)
                        unlockState = 1;
                    else if(value == 0xAA && unlockState == 1)
                        unlockState = 2;
                    else
                        unlockState = 0;
                    return;

                case 0x02:
                    eedata = value;
                    return;

                case 0x03:
                    eeadr = value;
                    return;
            }
        }

        public void Reset()
        {
            eecon1 = 0;
            eedata = 0;
            eeadr = 0;
            unlockState = 0;
            Array.Clear(storage, 0, storage.Length);
        }

        public long Size => 0x04;
        public GPIO IRQ { get; }

        private const byte RD   = 0x01;
        private const byte WR   = 0x02;
        private const byte WREN = 0x04;
        private const byte EEIF = 0x10;

        private byte eecon1;
        private byte eedata;
        private byte eeadr;
        private int unlockState;
        private readonly int eepromSize;
        private readonly byte[] storage;
    }
}
