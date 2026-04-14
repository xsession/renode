//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC33 DMA Controller peripheral (simplified model).
//
// Register map (word-wide):
//   0x00  DMAxCON  — DMA Channel Control (CHEN, SIZE, DIR, HALF, NULLW, AMODE, MODE)
//   0x02  DMAxREQ  — DMA Request Source (FORCE, IRQSEL)
//   0x04  DMAxSTAL — DMA SFR/RAM Start Address A (low word)
//   0x06  DMAxSTAH — DMA SFR/RAM Start Address A (high word, if 32-bit addressing)
//   0x08  DMAxSTBL — DMA SFR/RAM Start Address B (low word)
//   0x0A  DMAxSTBH — DMA SFR/RAM Start Address B (high word)
//   0x0C  DMAxPAD  — Peripheral Address
//   0x0E  DMAxCNT  — DMA Transfer Count
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.DMA
{
    public class dsPIC33_DMA : IWordPeripheral, IKnownSize
    {
        public dsPIC33_DMA(IMachine machine)
        {
            this.machine = machine;
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.DMAxCON:  return con;
            case Registers.DMAxREQ:  return req;
            case Registers.DMAxSTAL: return stal;
            case Registers.DMAxSTAH: return stah;
            case Registers.DMAxSTBL: return stbl;
            case Registers.DMAxSTBH: return stbh;
            case Registers.DMAxPAD:  return pad;
            case Registers.DMAxCNT:  return cnt;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_DMA: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.DMAxCON:
                con = value;
                if((value & CON_CHEN) != 0 && (value & CON_FORCE) != 0)
                {
                    /* Force transfer */
                    PerformTransfer();
                }
                break;
            case Registers.DMAxREQ:
                req = value;
                if((value & REQ_FORCE) != 0)
                {
                    PerformTransfer();
                    req &= unchecked((ushort)~REQ_FORCE);
                }
                break;
            case Registers.DMAxSTAL: stal = value; break;
            case Registers.DMAxSTAH: stah = value; break;
            case Registers.DMAxSTBL: stbl = value; break;
            case Registers.DMAxSTBH: stbh = value; break;
            case Registers.DMAxPAD:  pad = value;  break;
            case Registers.DMAxCNT:  cnt = value;  break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_DMA: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con  = 0;
            req  = 0;
            stal = 0;
            stah = 0;
            stbl = 0;
            stbh = 0;
            pad  = 0;
            cnt  = 0;
            transferCount = 0;
            IRQ.Unset();
        }

        public long Size => 0x10;
        public GPIO IRQ { get; }

        /// <summary>Trigger a DMA request from a peripheral IRQ source.</summary>
        public void TriggerRequest()
        {
            if((con & CON_CHEN) != 0)
            {
                PerformTransfer();
            }
        }

        private void PerformTransfer()
        {
            if((con & CON_CHEN) == 0) return;

            /* Simplified: just signal completion */
            transferCount++;
            if(transferCount > cnt)
            {
                transferCount = 0;
                /* Half-buffer or full-buffer interrupt */
                IRQ.Set();
                IRQ.Unset();

                /* In one-shot mode, disable channel */
                int mode = con & 0x03;
                if(mode == 0x01)
                {
                    con &= unchecked((ushort)~CON_CHEN);
                }
            }

            this.Log(LogLevel.Debug, "dsPIC33_DMA: Transfer #{0}, STA=0x{1:X4}, PAD=0x{2:X4}",
                     transferCount, stal, pad);
        }

        private enum Registers : long
        {
            DMAxCON  = 0x00,
            DMAxREQ  = 0x02,
            DMAxSTAL = 0x04,
            DMAxSTAH = 0x06,
            DMAxSTBL = 0x08,
            DMAxSTBH = 0x0A,
            DMAxPAD  = 0x0C,
            DMAxCNT  = 0x0E,
        }

        private const ushort CON_CHEN  = (ushort)(1 << 15);
        private const ushort CON_FORCE = (1 << 14);
        private const ushort REQ_FORCE = (ushort)(1 << 15);

        private ushort con;
        private ushort req;
        private ushort stal;
        private ushort stah;
        private ushort stbl;
        private ushort stbh;
        private ushort pad;
        private ushort cnt;
        private int transferCount;

        private readonly IMachine machine;
    }
}
