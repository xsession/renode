//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC33 I2C peripheral (I2Cx module).
//
// Register map (word-wide):
//   0x00  I2CxCON   — I2C Control (I2CEN, I2CSIDL, SCLREL, IPMIEN, A10M, DISSLW,
//                      SMEN, GCEN, STREN, ACKDT, ACKEN, RCEN, PEN, RSEN, SEN)
//   0x02  I2CxSTAT  — I2C Status (ACKSTAT, TRSTAT, BCL, GCSTAT, ADD10, IWCOL,
//                      I2COV, D/~A, P, S, R/~W, RBF, TBF)
//   0x04  I2CxADD   — I2C Address
//   0x06  I2CxMSK   — I2C Address Mask
//   0x08  I2CxBRG   — Baud Rate Generator
//   0x0A  I2CxTRN   — Transmit
//   0x0C  I2CxRCV   — Receive
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class dsPIC33_I2C : IWordPeripheral, IKnownSize
    {
        public dsPIC33_I2C(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.I2CxCON:  return con;
            case Registers.I2CxSTAT: return stat;
            case Registers.I2CxADD:  return add;
            case Registers.I2CxMSK:  return msk;
            case Registers.I2CxBRG:  return brg;
            case Registers.I2CxTRN:  return trn;
            case Registers.I2CxRCV:
                stat &= unchecked((ushort)~STAT_RBF);
                return rcv;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_I2C: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.I2CxCON:
                con = value;
                HandleControl(value);
                break;
            case Registers.I2CxSTAT:
                /* BCL and IWCOL are write-0-to-clear */
                if((value & STAT_BCL) == 0) stat &= unchecked((ushort)~STAT_BCL);
                if((value & STAT_IWCOL) == 0) stat &= unchecked((ushort)~STAT_IWCOL);
                break;
            case Registers.I2CxADD:
                add = value;
                break;
            case Registers.I2CxMSK:
                msk = value;
                break;
            case Registers.I2CxBRG:
                brg = value;
                break;
            case Registers.I2CxTRN:
                trn = value;
                stat |= STAT_TBF;
                /* Immediate transfer: clear TBF after "send" */
                stat &= unchecked((ushort)~STAT_TBF);
                IRQ.Set(); IRQ.Unset();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_I2C: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con  = 0;
            stat = 0;
            add  = 0;
            msk  = 0;
            brg  = 0;
            trn  = 0xFF;
            rcv  = 0xFF;
            IRQ.Unset();
        }

        public long Size => 0x0E;
        public GPIO IRQ { get; }

        private void HandleControl(ushort value)
        {
            /* SEN: Start */
            if((value & CON_SEN) != 0)
            {
                stat |= STAT_S;
                con &= unchecked((ushort)~CON_SEN);
                IRQ.Set(); IRQ.Unset();
            }
            /* PEN: Stop */
            if((value & CON_PEN) != 0)
            {
                stat |= STAT_P;
                stat &= unchecked((ushort)~STAT_S);
                con &= unchecked((ushort)~CON_PEN);
                IRQ.Set(); IRQ.Unset();
            }
            /* RCEN: Receive */
            if((value & CON_RCEN) != 0)
            {
                con &= unchecked((ushort)~CON_RCEN);
                rcv = 0xFF;
                stat |= STAT_RBF;
                IRQ.Set(); IRQ.Unset();
            }
            /* ACKEN: Acknowledge */
            if((value & CON_ACKEN) != 0)
            {
                con &= unchecked((ushort)~CON_ACKEN);
                IRQ.Set(); IRQ.Unset();
            }
        }

        private enum Registers : long
        {
            I2CxCON  = 0x00,
            I2CxSTAT = 0x02,
            I2CxADD  = 0x04,
            I2CxMSK  = 0x06,
            I2CxBRG  = 0x08,
            I2CxTRN  = 0x0A,
            I2CxRCV  = 0x0C,
        }

        /* CON bits */
        private const ushort CON_SEN   = (1 << 0);
        private const ushort CON_RSEN  = (1 << 1);
        private const ushort CON_PEN   = (1 << 2);
        private const ushort CON_RCEN  = (1 << 3);
        private const ushort CON_ACKEN = (1 << 4);
        private const ushort CON_I2CEN = (ushort)(1 << 15);

        /* STAT bits */
        private const ushort STAT_TBF   = (1 << 0);
        private const ushort STAT_RBF   = (1 << 1);
        private const ushort STAT_S     = (1 << 3);
        private const ushort STAT_P     = (1 << 4);
        private const ushort STAT_IWCOL = (1 << 7);
        private const ushort STAT_BCL   = (1 << 10);

        private ushort con;
        private ushort stat;
        private ushort add;
        private ushort msk;
        private ushort brg;
        private ushort trn;
        private ushort rcv;
    }
}
