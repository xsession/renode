//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC30 I2C peripheral (I2C module).
//
// Register map (word-wide, dsPIC30F Family Reference Manual, Section 16):
//   0x00  I2CRCV   — Receive register (read)
//   0x02  I2CTRN   — Transmit register (write)
//   0x04  I2CBRG   — Baud rate generator
//   0x06  I2CCON   — Control (I2CEN, I2CSIDL, SCLREL, IPMIEN, A10M, DISSLW, SMEN,
//                     GCEN, STREN, ACKDT, ACKEN, RCEN, PEN, RSEN, SEN)
//   0x08  I2CSTAT  — Status (ACKSTAT, TRSTAT, BCL, GCSTAT, ADD10, IWCOL,
//                     I2COV, D_A, P, S, R_W, RBF, TBF)
//   0x0A  I2CADD   — Address register (7-bit or 10-bit slave address)
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class dsPIC30_I2C : IWordPeripheral, IKnownSize
    {
        public dsPIC30_I2C(IMachine machine)
        {
            IRQ = new GPIO();
            rxFifo = new Queue<byte>();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.I2CRCV:
                if(rxFifo.Count > 0)
                {
                    stat &= unchecked((ushort)~STAT_RBF);
                    return rxFifo.Dequeue();
                }
                return 0;
            case Registers.I2CTRN:  return 0;
            case Registers.I2CBRG:  return brg;
            case Registers.I2CCON:  return con;
            case Registers.I2CSTAT: return stat;
            case Registers.I2CADD:  return add;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_I2C: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.I2CTRN:
                if((con & CON_I2CEN) == 0) break;
                /* TX immediate completion */
                stat &= unchecked((ushort)~STAT_TBF);
                stat &= unchecked((ushort)~STAT_TRSTAT);
                IRQ.Set();
                IRQ.Unset();
                break;
            case Registers.I2CBRG:
                brg = value;
                break;
            case Registers.I2CCON:
                con = value;
                if((value & CON_SEN) != 0 || (value & CON_RSEN) != 0)
                {
                    /* Start / Repeated-Start initiated */
                    con &= unchecked((ushort)~(CON_SEN | CON_RSEN));
                    IRQ.Set();
                    IRQ.Unset();
                }
                if((value & CON_PEN) != 0)
                {
                    con &= unchecked((ushort)~CON_PEN);
                    IRQ.Set();
                    IRQ.Unset();
                }
                if((value & CON_RCEN) != 0)
                {
                    /* Receive mode — produce dummy byte */
                    rxFifo.Enqueue(0xFF);
                    stat |= STAT_RBF;
                    con &= unchecked((ushort)~CON_RCEN);
                    IRQ.Set();
                    IRQ.Unset();
                }
                break;
            case Registers.I2CSTAT:
                /* BCL, IWCOL, I2COV: write-0-to-clear */
                if((value & STAT_BCL) == 0)  stat &= unchecked((ushort)~STAT_BCL);
                if((value & STAT_IWCOL) == 0) stat &= unchecked((ushort)~STAT_IWCOL);
                if((value & STAT_I2COV) == 0) stat &= unchecked((ushort)~STAT_I2COV);
                break;
            case Registers.I2CADD:
                add = (ushort)(value & 0x3FF);
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_I2C: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con  = 0;
            stat = 0;
            brg  = 0;
            add  = 0;
            rxFifo.Clear();
            IRQ.Unset();
        }

        public long Size => 0x0C;
        public GPIO IRQ { get; }

        private enum Registers : long
        {
            I2CRCV  = 0x00,
            I2CTRN  = 0x02,
            I2CBRG  = 0x04,
            I2CCON  = 0x06,
            I2CSTAT = 0x08,
            I2CADD  = 0x0A,
        }

        private const ushort CON_I2CEN = (1 << 15);
        private const ushort CON_SEN   = (1 << 0);
        private const ushort CON_RSEN  = (1 << 1);
        private const ushort CON_PEN   = (1 << 2);
        private const ushort CON_RCEN  = (1 << 3);
        private const ushort STAT_TBF    = (1 << 0);
        private const ushort STAT_RBF    = (1 << 1);
        private const ushort STAT_TRSTAT = (1 << 14);
        private const ushort STAT_BCL    = (1 << 10);
        private const ushort STAT_IWCOL  = (1 << 7);
        private const ushort STAT_I2COV  = (1 << 6);

        private ushort con, stat, brg, add;
        private readonly Queue<byte> rxFifo;
    }
}
