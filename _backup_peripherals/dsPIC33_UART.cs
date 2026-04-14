//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// dsPIC33 UART peripheral (UARTx module).
//
// Register map (word-wide, per the dsPIC33 Family Reference Manual):
//   0x00  UxMODE  — UART mode
//   0x02  UxSTA   — UART status & control
//   0x04  UxTXREG — transmit register (write)
//   0x06  UxRXREG — receive register (read)
//   0x08  UxBRG   — baud rate generator
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class dsPIC33_UART : IUART, IWordPeripheral, IKnownSize
    {
        public dsPIC33_UART(IMachine machine)
        {
            IRQ = new GPIO();
            rxQueue = new Queue<byte>();

            BuildRegisters();
        }

        // ------------------------------------------------------------------
        // IWordPeripheral
        // ------------------------------------------------------------------

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.UxMODE:  return mode;
            case Registers.UxSTA:   UpdateSTA(); return sta;
            case Registers.UxRXREG: return ReadRxReg();
            case Registers.UxBRG:   return brg;
            default:
                this.Log(LogLevel.Warning, "Read from unknown register at offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.UxMODE:
                mode = value;
                uartEnabled = (value & MODE_UARTEN) != 0;
                break;

            case Registers.UxSTA:
                // URXISEL and UTXISEL are read-write; TRMT/URXDA are read-only
                sta = (ushort)((sta & STA_READONLY_MASK) | (value & ~STA_READONLY_MASK));
                UpdateIRQ();
                break;

            case Registers.UxTXREG:
                if(uartEnabled) {
                    CharReceived?.Invoke((byte)(value & 0xFF));
                }
                // TRMT (bit 8) stays set — single-byte FIFO always empty after write
                sta |= STA_TRMT;
                UpdateIRQ();
                break;

            case Registers.UxBRG:
                brg = value;
                break;

            default:
                this.Log(LogLevel.Warning, "Write 0x{0:X4} to unknown register at offset 0x{1:X}", value, offset);
                break;
            }
        }

        // ------------------------------------------------------------------
        // IUART
        // ------------------------------------------------------------------

        public void WriteChar(byte character)
        {
            rxQueue.Enqueue(character);
            sta |= STA_URXDA;
            UpdateIRQ();
        }

        public void Reset()
        {
            mode = 0;
            sta  = STA_TRMT;    // transmitter always empty after reset
            brg  = 0;
            rxQueue.Clear();
            uartEnabled = false;
            IRQ.Unset();
        }

        public GPIO   IRQ       { get; }
        public long   Size      => 0x0A;
        public uint   BaudRate  => 0;
        public Bits   StopBits  => Bits.One;
        public Parity ParityBit => Parity.None;

        public event Action<byte> CharReceived;

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private ushort ReadRxReg()
        {
            if(rxQueue.Count > 0)
            {
                var ch = rxQueue.Dequeue();
                if(rxQueue.Count == 0)
                {
                    sta &= unchecked((ushort)~STA_URXDA);
                }
                UpdateIRQ();
                return ch;
            }
            return 0;
        }

        private void UpdateSTA()
        {
            // URXDA reflects whether the queue has data
            if(rxQueue.Count > 0)
                sta |= STA_URXDA;
            else
                sta &= unchecked((ushort)~STA_URXDA);
        }

        private void UpdateIRQ()
        {
            bool txIrq = (sta & STA_UTXEN) != 0 && (sta & STA_TRMT) != 0;
            bool rxIrq = (sta & STA_URXDA) != 0;
            IRQ.Set(txIrq || rxIrq);
        }

        private void BuildRegisters() { Reset(); }

        // ------------------------------------------------------------------
        // Register layout
        // ------------------------------------------------------------------

        private enum Registers : long
        {
            UxMODE  = 0x00,
            UxSTA   = 0x02,
            UxTXREG = 0x04,
            UxRXREG = 0x06,
            UxBRG   = 0x08,
        }

        // UxMODE bits
        private const ushort MODE_UARTEN = 1 << 15;

        // UxSTA bits
        private const ushort STA_URXDA       = 1 << 0;  // Receive data available (RO)
        private const ushort STA_OERR        = 1 << 1;  // Overrun error           (RC)
        private const ushort STA_FERR        = 1 << 2;  // Framing error           (RO)
        private const ushort STA_PERR        = 1 << 3;  // Parity error            (RO)
        private const ushort STA_RIDLE       = 1 << 4;  // Receiver idle           (RO)
        private const ushort STA_ADDEN       = 1 << 5;  // Address character detect
        private const ushort STA_URXISEL0    = 1 << 6;  // IRQ mode select [0]
        private const ushort STA_URXISEL1    = 1 << 7;  // IRQ mode select [1]
        private const ushort STA_TRMT        = 1 << 8;  // Transmit shift reg empty (RO)
        private const ushort STA_UTXBF       = 1 << 9;  // TX buffer full           (RO)
        private const ushort STA_UTXEN       = 1 << 10; // Transmit enable
        private const ushort STA_UTXBRK      = 1 << 11; // Transmit break
        private const ushort STA_URXEN       = 1 << 12; // Receive enable
        private const ushort STA_UTXISEL0    = 1 << 13; // TX IRQ select [0]
        private const ushort STA_UTXINV      = 1 << 14; // TX polarity invert
        private const ushort STA_UTXISEL1    = 1 << 15; // TX IRQ select [1]

        // Read-only bits in STA (cannot be cleared by software write)
        private const ushort STA_READONLY_MASK = STA_URXDA | STA_OERR | STA_FERR | STA_PERR |
                                                  STA_RIDLE | STA_TRMT | STA_UTXBF;

        private ushort mode;
        private ushort sta  = STA_TRMT;
        private ushort brg;
        private bool   uartEnabled;

        private readonly Queue<byte> rxQueue;
    }
}
