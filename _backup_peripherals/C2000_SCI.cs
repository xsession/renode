//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x SCI (Serial Communications Interface) peripheral.
//
// Register map (16-bit word addresses, byte offsets shown):
//   0x00  SCICCR    — SCI communications control register
//   0x02  SCICTL1   — SCI control register 1
//   0x04  SCIHBAUD  — SCI baud rate high byte
//   0x06  SCILBAUD  — SCI baud rate low byte
//   0x08  SCICTL2   — SCI control register 2
//   0x0A  SCIRXST   — SCI receiver status register (read-only)
//   0x0C  SCIRXEMU  — SCI receive data buffer (emu)
//   0x0E  SCIRXBUF  — SCI receiver data buffer
//   0x10  SCITXBUF  — SCI transmit data buffer (write)
//   0x12  SCIFFTX   — SCI FIFO transmit register
//   0x14  SCIFFRX   — SCI FIFO receive register
//   0x16  SCIFFCT   — SCI FIFO control register
//   0x1A  SCIPRI    — SCI priority control
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class C2000_SCI : IUART, IWordPeripheral, IKnownSize
    {
        public C2000_SCI(IMachine machine)
        {
            IRQ = new GPIO();
            rxQueue = new Queue<byte>();
        }

        // ------------------------------------------------------------------
        // IWordPeripheral
        // ------------------------------------------------------------------

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SCICCR:   return sciccr;
            case Registers.SCICTL1:  return scictl1;
            case Registers.SCIHBAUD: return scihbaud;
            case Registers.SCILBAUD: return scilbaud;
            case Registers.SCICTL2:  return scictl2;
            case Registers.SCIRXST:  return BuildRxStatus();
            case Registers.SCIRXEMU: return PeekRx();
            case Registers.SCIRXBUF: return ReadRxBuf();
            case Registers.SCITXBUF: return 0;
            case Registers.SCIFFTX:  return scifftx;
            case Registers.SCIFFRX:  return BuildFfrx();
            case Registers.SCIFFCT:  return sciffct;
            case Registers.SCIPRI:   return scipri;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_SCI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.SCICCR:
                sciccr = value;
                break;
            case Registers.SCICTL1:
                scictl1 = value;
                sciEnabled = (value & SCICTL1_TXENA) != 0 ||
                             (value & SCICTL1_RXENA) != 0;
                break;
            case Registers.SCIHBAUD:
                scihbaud = value;
                break;
            case Registers.SCILBAUD:
                scilbaud = value;
                break;
            case Registers.SCICTL2:
                scictl2 = (ushort)(value & ~SCICTL2_READONLY_MASK);
                break;
            case Registers.SCIRXBUF:
                /* read-only; ignore writes */
                break;
            case Registers.SCITXBUF:
                Transmit((byte)(value & 0xFF));
                break;
            case Registers.SCIFFTX:
                scifftx = value;
                break;
            case Registers.SCIFFRX:
                sciffrx = value;
                break;
            case Registers.SCIFFCT:
                sciffct = value;
                break;
            case Registers.SCIPRI:
                scipri = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_SCI: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            sciccr   = 0x0007; /* 8-bit, 1 stop, no parity, async */
            scictl1  = 0x0023; /* TXENA + RXENA + SW_RESET */
            scihbaud = 0;
            scilbaud = 0;
            scictl2  = 0;
            scifftx  = 0x6000;
            sciffrx  = 0x2022;
            sciffct  = 0;
            scipri   = 0x0010;
            rxQueue.Clear();
            IRQ.Unset();
            sciEnabled = true;
        }

        // ------------------------------------------------------------------
        // IUART
        // ------------------------------------------------------------------

        public void WriteChar(byte value)
        {
            if(!sciEnabled)
            {
                return;
            }
            rxQueue.Enqueue(value);
            UpdateIRQ();
        }

        public event Action<byte> CharReceived;

        public uint BaudRate
        {
            get
            {
                uint brr = (uint)(((ushort)scihbaud << 8) | (ushort)scilbaud);
                if(brr == 0)
                {
                    return 115200;
                }
                /* BRR = (LSPCLK / (SCI_BRR + 1)) / 8 */
                const uint lspclk = 50000000; /* 50 MHz default LSPCLK */
                return lspclk / ((brr + 1) * 8);
            }
        }

        public Bits StopBits => Bits.One;

        public Parity ParityBit =>
            (sciccr & SCICCR_PARITY_ENA) == 0 ? Parity.None :
            (sciccr & SCICCR_PARITY)     != 0 ? Parity.Odd  : Parity.Even;

        // ------------------------------------------------------------------
        // IKnownSize
        // ------------------------------------------------------------------

        public long Size => 0x1C;

        // ------------------------------------------------------------------
        // GPIO
        // ------------------------------------------------------------------

        public GPIO IRQ { get; }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private void Transmit(byte value)
        {
            CharReceived?.Invoke(value);
        }

        private ushort BuildRxStatus()
        {
            ushort st = SCIRXST_TXRDY; /* TX ready by default */
            if(rxQueue.Count > 0)
            {
                st |= SCIRXST_RXRDY;
            }
            return st;
        }

        private ushort PeekRx()
        {
            if(rxQueue.Count == 0)
            {
                return 0;
            }
            /* Peek without dequeue */
            foreach(var b in rxQueue) { return b; }
            return 0;
        }

        private ushort ReadRxBuf()
        {
            if(rxQueue.Count == 0)
            {
                return 0;
            }
            var data = rxQueue.Dequeue();
            UpdateIRQ();
            return data;
        }

        private ushort BuildFfrx()
        {
            ushort v = sciffrx;
            /* RXFFST (bits 12:8) = current FIFO level (max 16) */
            int level = Math.Min(rxQueue.Count, 16);
            v = (ushort)((v & 0xE0FF) | (level << 8));
            return v;
        }

        private void UpdateIRQ()
        {
            bool rxIrqEna  = (scictl2 & SCICTL2_RXBKINTENA) != 0 ||
                             (scictl1 & SCICTL1_RXENA)      != 0;
            bool txIrqEna  = (scictl2 & SCICTL2_TXINTENA)   != 0;

            bool fireIrq   = (rxIrqEna && rxQueue.Count > 0) || txIrqEna;

            IRQ.Set(fireIrq);
        }

        // ------------------------------------------------------------------
        // Register layout
        // ------------------------------------------------------------------

        private enum Registers : long
        {
            SCICCR   = 0x00,
            SCICTL1  = 0x02,
            SCIHBAUD = 0x04,
            SCILBAUD = 0x06,
            SCICTL2  = 0x08,
            SCIRXST  = 0x0A,
            SCIRXEMU = 0x0C,
            SCIRXBUF = 0x0E,
            SCITXBUF = 0x10,
            SCIFFTX  = 0x12,
            SCIFFRX  = 0x14,
            SCIFFCT  = 0x16,
            SCIPRI   = 0x1A,
        }

        // ------------------------------------------------------------------
        // Register bit constants
        // ------------------------------------------------------------------

        /* SCICCR */
        private const ushort SCICCR_PARITY_ENA = (1 << 5);
        private const ushort SCICCR_PARITY     = (1 << 4);

        /* SCICTL1 */
        private const ushort SCICTL1_TXENA     = (1 << 1);
        private const ushort SCICTL1_RXENA     = (1 << 2);

        /* SCICTL2 */
        private const ushort SCICTL2_TXINTENA    = (1 << 0);
        private const ushort SCICTL2_RXBKINTENA  = (1 << 1);
        private const ushort SCICTL2_READONLY_MASK = (1 << 7); /* TXEMPTY */

        /* SCIRXST */
        private const ushort SCIRXST_RXRDY = (1 << 6);
        private const ushort SCIRXST_TXRDY = (1 << 7);

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private ushort sciccr;
        private ushort scictl1;
        private ushort scihbaud;
        private ushort scilbaud;
        private ushort scictl2;
        private ushort scifftx;
        private ushort sciffrx;
        private ushort sciffct;
        private ushort scipri;

        private bool sciEnabled;

        private readonly Queue<byte> rxQueue;
    }
}
