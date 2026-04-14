//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC16 MSSP I2C peripheral (Master Synchronous Serial Port — I2C mode).
//
// Register map (byte-wide):
//   0x00  SSPCON   — SSP Control
//   0x01  SSPCON2  — SSP Control 2 (SEN, RSEN, PEN, RCEN, ACKEN, ACKDT, ACKSTAT)
//   0x02  SSPSTAT  — SSP Status
//   0x03  SSPADD   — SSP Address / Baud Rate
//   0x04  SSPBUF   — SSP Buffer
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class PIC16_MSSP_I2C : IBytePeripheral, IKnownSize
    {
        public PIC16_MSSP_I2C(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SSPCON:  return sspcon;
            case Registers.SSPCON2: return sspcon2;
            case Registers.SSPSTAT: return sspstat;
            case Registers.SSPADD: return sspadd;
            case Registers.SSPBUF:
                sspstat &= unchecked((byte)~SSPSTAT_BF);
                return sspbuf;
            default:
                this.Log(LogLevel.Warning,
                         "PIC16_MSSP_I2C: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.SSPCON:
                sspcon = value;
                break;
            case Registers.SSPCON2:
                sspcon2 = value;
                HandleI2CAction(value);
                break;
            case Registers.SSPSTAT:
                sspstat = (byte)((sspstat & 0x3F) | (value & 0xC0));
                break;
            case Registers.SSPADD:
                sspadd = value;
                break;
            case Registers.SSPBUF:
                sspbuf = value;
                if((sspcon & SSPCON_SSPEN) != 0)
                {
                    sspstat |= SSPSTAT_BF;
                    IRQ.Set();
                    IRQ.Unset();
                }
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC16_MSSP_I2C: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            sspcon  = 0;
            sspcon2 = 0;
            sspstat = 0;
            sspadd  = 0;
            sspbuf  = 0;
            IRQ.Unset();
        }

        public long Size => 0x05;
        public GPIO IRQ { get; }

        private void HandleI2CAction(byte value)
        {
            /* SEN: Start condition */
            if((value & SSPCON2_SEN) != 0)
            {
                sspstat |= SSPSTAT_S;
                sspcon2 &= unchecked((byte)~SSPCON2_SEN);
                IRQ.Set();
                IRQ.Unset();
            }
            /* PEN: Stop condition */
            if((value & SSPCON2_PEN) != 0)
            {
                sspstat |= SSPSTAT_P;
                sspstat &= unchecked((byte)~SSPSTAT_S);
                sspcon2 &= unchecked((byte)~SSPCON2_PEN);
                IRQ.Set();
                IRQ.Unset();
            }
            /* RCEN: Receive enable */
            if((value & SSPCON2_RCEN) != 0)
            {
                sspcon2 &= unchecked((byte)~SSPCON2_RCEN);
                sspstat |= SSPSTAT_BF;
                sspbuf = 0xFF; /* No real slave, return 0xFF */
                IRQ.Set();
                IRQ.Unset();
            }
            /* ACKEN: Acknowledge sequence */
            if((value & SSPCON2_ACKEN) != 0)
            {
                sspcon2 &= unchecked((byte)~SSPCON2_ACKEN);
                IRQ.Set();
                IRQ.Unset();
            }
        }

        private enum Registers : long
        {
            SSPCON  = 0x00,
            SSPCON2 = 0x01,
            SSPSTAT = 0x02,
            SSPADD  = 0x03,
            SSPBUF  = 0x04,
        }

        private const byte SSPCON_SSPEN   = (1 << 5);
        private const byte SSPSTAT_BF     = (1 << 0);
        private const byte SSPSTAT_S      = (1 << 3);
        private const byte SSPSTAT_P      = (1 << 4);
        private const byte SSPCON2_SEN    = (1 << 0);
        private const byte SSPCON2_RSEN   = (1 << 1);
        private const byte SSPCON2_PEN    = (1 << 2);
        private const byte SSPCON2_RCEN   = (1 << 3);
        private const byte SSPCON2_ACKEN  = (1 << 4);

        private byte sspcon;
        private byte sspcon2;
        private byte sspstat;
        private byte sspadd;
        private byte sspbuf;
    }
}
