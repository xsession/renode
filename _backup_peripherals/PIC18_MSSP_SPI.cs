//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC18 MSSP SPI peripheral (Enhanced SPI mode).
//
// Register map (byte-wide):
//   0x00  SSPCON1 — SSP Control 1 (WCOL, SSPOV, SSPEN, CKP, SSPM3:0)
//   0x01  SSPSTAT — SSP Status (SMP, CKE, D/~A, P, S, R/~W, UA, BF)
//   0x02  SSPBUF  — SSP Buffer
//   0x03  SSPADD  — Address / Baud Rate
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class PIC18_MSSP_SPI : IBytePeripheral, IKnownSize
    {
        public PIC18_MSSP_SPI(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SSPCON1: return sspcon1;
            case Registers.SSPSTAT: return sspstat;
            case Registers.SSPBUF:
                sspstat &= unchecked((byte)~SSPSTAT_BF);
                return rxData;
            case Registers.SSPADD: return sspadd;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_MSSP_SPI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.SSPCON1:
                sspcon1 = value;
                break;
            case Registers.SSPSTAT:
                sspstat = (byte)((sspstat & 0x3F) | (value & 0xC0));
                break;
            case Registers.SSPBUF:
                if((sspcon1 & SSPCON1_SSPEN) == 0) break;
                txData = value;
                DoTransfer();
                break;
            case Registers.SSPADD:
                sspadd = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_MSSP_SPI: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            sspcon1 = 0;
            sspstat = 0;
            sspadd  = 0;
            rxData  = 0;
            txData  = 0;
            IRQ.Unset();
        }

        public long Size => 0x04;
        public GPIO IRQ { get; }

        public event Action<byte, byte> TransferCompleted;

        private void DoTransfer()
        {
            rxData = 0xFF;
            sspstat |= SSPSTAT_BF;
            IRQ.Set();
            IRQ.Unset();
            TransferCompleted?.Invoke(txData, rxData);
        }

        private enum Registers : long
        {
            SSPCON1 = 0x00,
            SSPSTAT = 0x01,
            SSPBUF  = 0x02,
            SSPADD  = 0x03,
        }

        private const byte SSPCON1_SSPEN = (1 << 5);
        private const byte SSPSTAT_BF    = (1 << 0);

        private byte sspcon1;
        private byte sspstat;
        private byte sspadd;
        private byte rxData;
        private byte txData;
    }
}
