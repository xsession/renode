//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC30 SPI peripheral (SPIx module).
//
// Register map (word-wide, dsPIC30F Family Reference Manual, Section 18):
//   0x00  SPIxSTAT — SPI status (SPIEN, SPISIDL, SPIROV, SPITBF, SPIRBF)
//   0x02  SPIxCON  — SPI control (FRMEN, SPIFSD, DISSDO, MODE16, SMP, CKE, SSEN, CKP,
//                     MSTEN, SPRE, PPRE)
//   0x04  SPIxBUF  — SPI buffer (read=RX, write=TX)
//
// Note: dsPIC30 uses SPIxSTAT at offset 0x00 (vs dsPIC33 which has SPIxCON1 at 0x00).
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class dsPIC30_SPI : IWordPeripheral, IKnownSize
    {
        public dsPIC30_SPI(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SPIxSTAT:
                return stat;
            case Registers.SPIxCON:
                return con;
            case Registers.SPIxBUF:
                stat &= unchecked((ushort)~STAT_SPIRBF);
                return rxBuf;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_SPI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.SPIxSTAT:
                /* SPIEN writable; SPIROV write-0-to-clear */
                if((value & STAT_SPIROV) == 0)
                    stat &= unchecked((ushort)~STAT_SPIROV);
                if((value & STAT_SPIEN) != 0)
                    stat |= STAT_SPIEN;
                else
                    stat &= unchecked((ushort)~STAT_SPIEN);
                break;
            case Registers.SPIxCON:
                con = value;
                break;
            case Registers.SPIxBUF:
                if((stat & STAT_SPIEN) == 0) break;
                txBuf = value;
                DoTransfer();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_SPI: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            stat  = 0;
            con   = 0;
            rxBuf = 0;
            txBuf = 0;
            IRQ.Unset();
        }

        public long Size => 0x06;
        public GPIO IRQ { get; }

        public event Action<ushort, ushort> TransferCompleted;

        private void DoTransfer()
        {
            rxBuf = 0xFFFF;
            stat |= STAT_SPIRBF;
            stat &= unchecked((ushort)~STAT_SPITBF);
            IRQ.Set();
            IRQ.Unset();
            TransferCompleted?.Invoke(txBuf, rxBuf);
        }

        private enum Registers : long
        {
            SPIxSTAT = 0x00,
            SPIxCON  = 0x02,
            SPIxBUF  = 0x04,
        }

        private const ushort STAT_SPIRBF = (1 << 0);
        private const ushort STAT_SPITBF = (1 << 1);
        private const ushort STAT_SPIROV = (1 << 6);
        private const ushort STAT_SPIEN  = (1 << 15);

        private ushort stat, con, rxBuf, txBuf;
    }
}
