//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// TMS320C28x SPI peripheral.
//
// Register map (16-bit word addresses, byte offsets):
//   0x00  SPICCR    — SPI configuration control
//   0x02  SPICTL    — SPI operation control
//   0x04  SPISTS    — SPI status
//   0x06  SPIBRR    — SPI baud rate
//   0x08  SPIRXEMU  — SPI emulation buffer (read-only)
//   0x0A  SPIRXBUF  — SPI serial receive buffer
//   0x0C  SPITXBUF  — SPI serial transmit buffer
//   0x0E  SPIDAT    — SPI serial data (shift register)
//   0x10  SPIFFTX   — SPI FIFO transmit
//   0x12  SPIFFRX   — SPI FIFO receive
//   0x14  SPIFFCT   — SPI FIFO control
//   0x16  SPIPRI    — SPI priority control
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class C2000_SPI : IWordPeripheral, IKnownSize
    {
        public C2000_SPI(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SPICCR:   return spiccr;
            case Registers.SPICTL:   return spictl;
            case Registers.SPISTS:   return BuildStatus();
            case Registers.SPIBRR:   return spibrr;
            case Registers.SPIRXEMU: return rxBuf;
            case Registers.SPIRXBUF:
                var val = rxBuf;
                spiIntFlag = false;
                return val;
            case Registers.SPITXBUF: return txBuf;
            case Registers.SPIDAT:   return spidat;
            case Registers.SPIFFTX:  return spifftx;
            case Registers.SPIFFRX:  return spiffrx;
            case Registers.SPIFFCT:  return spiffct;
            case Registers.SPIPRI:   return spipri;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_SPI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.SPICCR:
                spiccr = value;
                if((value & CCR_RESET) == 0)
                    DoReset();
                break;
            case Registers.SPICTL:
                spictl = value;
                break;
            case Registers.SPIBRR:
                spibrr = (ushort)(value & 0x7F);
                break;
            case Registers.SPITXBUF:
                txBuf = value;
                if((spiccr & CCR_RESET) != 0)
                    DoTransfer();
                break;
            case Registers.SPIDAT:
                spidat = value;
                if((spiccr & CCR_RESET) != 0)
                    DoTransfer();
                break;
            case Registers.SPIFFTX:
                spifftx = value;
                break;
            case Registers.SPIFFRX:
                spiffrx = value;
                break;
            case Registers.SPIFFCT:
                spiffct = value;
                break;
            case Registers.SPIPRI:
                spipri = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_SPI: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            DoReset();
        }

        public long Size => 0x18;
        public GPIO IRQ { get; }

        public event Action<ushort, ushort> TransferCompleted;

        private void DoReset()
        {
            spiccr  = 0;
            spictl  = 0;
            spibrr  = 0;
            spifftx = 0;
            spiffrx = 0;
            spiffct = 0;
            spipri  = 0;
            txBuf   = 0;
            rxBuf   = 0;
            spidat  = 0;
            spiIntFlag = false;
            IRQ.Unset();
        }

        private void DoTransfer()
        {
            rxBuf = 0xFFFF; /* loopback or default */
            spiIntFlag = true;
            spidat = rxBuf;

            if((spictl & CTL_INT_ENA) != 0)
            {
                IRQ.Set();
                IRQ.Unset();
            }
            TransferCompleted?.Invoke(txBuf, rxBuf);
        }

        private ushort BuildStatus()
        {
            ushort st = STS_BUFFULL_CLR; /* buffer not full — ready */
            if(spiIntFlag)
                st |= STS_INT_FLAG;
            return st;
        }

        private enum Registers : long
        {
            SPICCR   = 0x00,
            SPICTL   = 0x02,
            SPISTS   = 0x04,
            SPIBRR   = 0x06,
            SPIRXEMU = 0x08,
            SPIRXBUF = 0x0A,
            SPITXBUF = 0x0C,
            SPIDAT   = 0x0E,
            SPIFFTX  = 0x10,
            SPIFFRX  = 0x12,
            SPIFFCT  = 0x14,
            SPIPRI   = 0x16,
        }

        private const ushort CCR_RESET      = (1 << 7);
        private const ushort CTL_INT_ENA    = (1 << 0);
        private const ushort STS_INT_FLAG   = (1 << 6);
        private const ushort STS_BUFFULL_CLR = 0;

        private ushort spiccr, spictl, spibrr;
        private ushort spifftx, spiffrx, spiffct, spipri;
        private ushort txBuf, rxBuf, spidat;
        private bool spiIntFlag;
    }
}
