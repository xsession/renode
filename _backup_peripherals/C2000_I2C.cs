//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// TMS320C28x I2C peripheral.
//
// Register map (16-bit word addresses, byte offsets):
//   0x00  I2COAR   — Own address
//   0x02  I2CIER   — Interrupt enable
//   0x04  I2CSTR   — Status register
//   0x06  I2CCLKL  — Clock low divider
//   0x08  I2CCLKH  — Clock high divider
//   0x0A  I2CCNT   — Data count
//   0x0C  I2CDRR   — Data receive register
//   0x0E  I2CSAR   — Slave address
//   0x10  I2CDXR   — Data transmit register
//   0x12  I2CMDR   — Mode register (MST, TRX, STT, STP, IRS, FREE, etc.)
//   0x14  I2CISRC  — Interrupt source
//   0x16  I2CPSC   — Prescaler
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class C2000_I2C : IWordPeripheral, IKnownSize
    {
        public C2000_I2C(IMachine machine)
        {
            IRQ = new GPIO();
            rxFifo = new Queue<byte>();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.I2COAR:  return oar;
            case Registers.I2CIER:  return ier;
            case Registers.I2CSTR:
                var s = str;
                str &= unchecked((ushort)~(STR_ARDY | STR_RRDY | STR_XRDY));
                return s;
            case Registers.I2CCLKL: return clkl;
            case Registers.I2CCLKH: return clkh;
            case Registers.I2CCNT:  return cnt;
            case Registers.I2CDRR:
                if(rxFifo.Count > 0)
                    return rxFifo.Dequeue();
                return 0;
            case Registers.I2CSAR:  return sar;
            case Registers.I2CDXR:  return 0;
            case Registers.I2CMDR:  return mdr;
            case Registers.I2CISRC: return isrc;
            case Registers.I2CPSC:  return psc;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_I2C: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.I2COAR:
                oar = (ushort)(value & 0x3FF);
                break;
            case Registers.I2CIER:
                ier = (ushort)(value & 0x7F);
                break;
            case Registers.I2CSTR:
                /* write-1-to-clear bits */
                str &= (ushort)~(value & STR_W1C_MASK);
                break;
            case Registers.I2CCLKL:
                clkl = value;
                break;
            case Registers.I2CCLKH:
                clkh = value;
                break;
            case Registers.I2CCNT:
                cnt = value;
                break;
            case Registers.I2CSAR:
                sar = (ushort)(value & 0x3FF);
                break;
            case Registers.I2CDXR:
                /* TX — immediate completion */
                str |= STR_XRDY;
                if((ier & IER_XRDY) != 0)
                {
                    IRQ.Set();
                    IRQ.Unset();
                }
                break;
            case Registers.I2CMDR:
                mdr = value;
                if((value & MDR_IRS) == 0)
                    DoReset();
                if((value & MDR_STT) != 0)
                {
                    str |= STR_ARDY;
                    if((value & MDR_TRX) == 0)
                    {
                        /* read mode: provide dummy data */
                        for(int i = 0; i < Math.Max(cnt, 1); i++)
                            rxFifo.Enqueue(0xFF);
                        str |= STR_RRDY;
                    }
                    else
                    {
                        str |= STR_XRDY;
                    }
                    if((ier & (IER_ARDY | IER_RRDY | IER_XRDY)) != 0)
                    {
                        IRQ.Set();
                        IRQ.Unset();
                    }
                }
                break;
            case Registers.I2CISRC:
                isrc = 0;
                break;
            case Registers.I2CPSC:
                psc = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_I2C: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            DoReset();
        }

        public long Size => 0x18;
        public GPIO IRQ { get; }

        private void DoReset()
        {
            oar  = 0;
            ier  = 0;
            str  = 0;
            clkl = 0;
            clkh = 0;
            cnt  = 0;
            sar  = 0;
            mdr  = 0;
            isrc = 0;
            psc  = 0;
            rxFifo.Clear();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            I2COAR  = 0x00,
            I2CIER  = 0x02,
            I2CSTR  = 0x04,
            I2CCLKL = 0x06,
            I2CCLKH = 0x08,
            I2CCNT  = 0x0A,
            I2CDRR  = 0x0C,
            I2CSAR  = 0x0E,
            I2CDXR  = 0x10,
            I2CMDR  = 0x12,
            I2CISRC = 0x14,
            I2CPSC  = 0x16,
        }

        private const ushort MDR_IRS  = (1 << 5);
        private const ushort MDR_STT  = (1 << 13);
        private const ushort MDR_TRX  = (1 << 9);
        private const ushort STR_ARDY = (1 << 2);
        private const ushort STR_RRDY = (1 << 3);
        private const ushort STR_XRDY = (1 << 4);
        private const ushort STR_W1C_MASK = 0x007C;
        private const ushort IER_ARDY = (1 << 2);
        private const ushort IER_RRDY = (1 << 3);
        private const ushort IER_XRDY = (1 << 4);

        private ushort oar, ier, str, clkl, clkh, cnt, sar, mdr, isrc, psc;
        private readonly Queue<byte> rxFifo;
    }
}
