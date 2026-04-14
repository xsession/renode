//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC16 internal EEPROM data memory peripheral.
//
// Register map (byte-wide):
//   0x00  EEDATA  — EEPROM Data register
//   0x01  EEADR   — EEPROM Address register
//   0x02  EECON1  — EEPROM Control 1 (EEPGD, WRERR, WREN, WR, RD)
//   0x03  EECON2  — EEPROM Control 2 (write unlock sequence register)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Memory
{
    public class PIC16_EEPROM : IBytePeripheral, IKnownSize
    {
        public PIC16_EEPROM(IMachine machine, int eepromSize = 256)
        {
            this.eepromSize = eepromSize;
            storage = new byte[eepromSize];
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.EEDATA: return eedata;
            case Registers.EEADR:  return eeadr;
            case Registers.EECON1: return eecon1;
            case Registers.EECON2: return 0; /* Always reads as 0 */
            default:
                this.Log(LogLevel.Warning,
                         "PIC16_EEPROM: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.EEDATA:
                eedata = value;
                break;
            case Registers.EEADR:
                eeadr = value;
                break;
            case Registers.EECON1:
                HandleCon1Write(value);
                break;
            case Registers.EECON2:
                HandleUnlockSequence(value);
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC16_EEPROM: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            eedata    = 0;
            eeadr     = 0;
            eecon1    = 0;
            unlockStep = 0;
            IRQ.Unset();
        }

        /// <summary>Get the raw EEPROM storage for external inspection.</summary>
        public byte[] GetStorage() => storage;

        public long Size => 0x04;
        public GPIO IRQ { get; }

        private void HandleCon1Write(byte value)
        {
            eecon1 = value;

            /* RD: initiate read */
            if((value & EECON1_RD) != 0)
            {
                if(eeadr < eepromSize)
                    eedata = storage[eeadr];
                else
                    eedata = 0xFF;
                eecon1 &= unchecked((byte)~EECON1_RD);
            }

            /* WR: initiate write (only if WREN and unlock sequence done) */
            if((value & EECON1_WR) != 0 && (value & EECON1_WREN) != 0 && unlockStep >= 2)
            {
                if(eeadr < eepromSize)
                    storage[eeadr] = eedata;
                eecon1 &= unchecked((byte)~EECON1_WR);
                unlockStep = 0;

                IRQ.Set();
                IRQ.Unset();
            }
        }

        private void HandleUnlockSequence(byte value)
        {
            /* PIC16 EEPROM write unlock: write 0x55 then 0xAA to EECON2 */
            if(unlockStep == 0 && value == 0x55)
                unlockStep = 1;
            else if(unlockStep == 1 && value == 0xAA)
                unlockStep = 2;
            else
                unlockStep = 0;
        }

        private enum Registers : long
        {
            EEDATA = 0x00,
            EEADR  = 0x01,
            EECON1 = 0x02,
            EECON2 = 0x03,
        }

        private const byte EECON1_RD   = (1 << 0);
        private const byte EECON1_WR   = (1 << 1);
        private const byte EECON1_WREN = (1 << 2);

        private byte eedata;
        private byte eeadr;
        private byte eecon1;
        private int  unlockStep;

        private readonly int eepromSize;
        private readonly byte[] storage;
    }
}
