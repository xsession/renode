//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC33 SPI peripheral (SPIx module).
//
// Register map (word-wide, dsPIC33 family reference):
//   0x00  SPIxCON1  — SPI Control 1
//   0x02  SPIxCON2  — SPI Control 2
//   0x04  SPIxSTAT  — SPI Status (SPIRBF, SPITBF, SPIROV, SPIEN)
//   0x06  SPIxBUF   — SPI Buffer (read=RX, write=TX)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class dsPIC33_SPI : IWordPeripheral, IKnownSize
    {
        public dsPIC33_SPI(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SPIxCON1: return con1;
            case Registers.SPIxCON2: return con2;
            case Registers.SPIxSTAT:
                UpdateStat();
                return stat;
            case Registers.SPIxBUF:
                stat &= unchecked((ushort)~STAT_SPIRBF);
                return rxBuf;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_SPI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.SPIxCON1:
                con1 = value;
                break;
            case Registers.SPIxCON2:
                con2 = value;
                break;
            case Registers.SPIxSTAT:
                /* SPIEN is writable; SPIROV write-0-to-clear */
                if((value & STAT_SPIROV) == 0)
                    stat &= unchecked((ushort)~STAT_SPIROV);
                if((value & STAT_SPIEN) != 0)
                    stat |= STAT_SPIEN;
                else
                    stat &= unchecked((ushort)~STAT_SPIEN);
                break;
            case Registers.SPIxBUF:
                if((stat & STAT_SPIEN) == 0) break;
                txBuf = value;
                DoTransfer();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_SPI: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con1  = 0;
            con2  = 0;
            stat  = 0;
            rxBuf = 0;
            txBuf = 0;
            IRQ.Unset();
        }

        public long Size => 0x08;
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

        private void UpdateStat()
        {
            /* SPITBF is always clear in this model (immediate transfer) */
        }

        private enum Registers : long
        {
            SPIxCON1 = 0x00,
            SPIxCON2 = 0x02,
            SPIxSTAT = 0x04,
            SPIxBUF  = 0x06,
        }

        private const ushort STAT_SPIRBF = (1 << 0);
        private const ushort STAT_SPITBF = (1 << 1);
        private const ushort STAT_SPIROV = (1 << 6);
        private const ushort STAT_SPIEN  = (ushort)(1 << 15);

        private ushort con1;
        private ushort con2;
        private ushort stat;
        private ushort rxBuf;
        private ushort txBuf;
    }
}
