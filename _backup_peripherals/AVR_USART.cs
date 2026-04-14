//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// AVR USART peripheral (ATmega-series USART0/1/2/3).
//
// Register map (8-bit I/O, byte offsets from peripheral base):
//   0x00  UCSRnA — status/control A
//   0x01  UCSRnB — control B (enable TX/RX, interrupt enables)
//   0x02  UCSRnC — control C (frame format)
//   0x04  UBRRnL — baud rate low
//   0x05  UBRRnH — baud rate high
//   0x06  UDRn   — data register (read=RX, write=TX)
//
// Note: All registers are mapped as byte-wide in data memory.
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class AVR_USART : IUART, IBytePeripheral, IKnownSize
    {
        public AVR_USART(IMachine machine, long baseFrequency = 16_000_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ_RX = new GPIO();
            IRQ_TX = new GPIO();
            rxQueue = new Queue<byte>();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.UCSRnA: return BuildUCSRA();
            case Registers.UCSRnB: return ucsrB;
            case Registers.UCSRnC: return ucsrC;
            case Registers.UBRRnL: return (byte)(ubrr & 0xFF);
            case Registers.UBRRnH: return (byte)((ubrr >> 8) & 0x0F);
            case Registers.UDRn:   return ReadRX();
            default:
                this.Log(LogLevel.Warning,
                         "AVR_USART: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.UCSRnA:
                /* TXC is write-1-to-clear, others unused on write */
                if((value & UCSRA_TXC) != 0)
                {
                    ucsrA &= unchecked((byte)~UCSRA_TXC);
                }
                break;
            case Registers.UCSRnB:
                ucsrB = value;
                break;
            case Registers.UCSRnC:
                ucsrC = value;
                break;
            case Registers.UBRRnL:
                ubrr = (ushort)((ubrr & 0xF00) | value);
                break;
            case Registers.UBRRnH:
                ubrr = (ushort)((ubrr & 0x0FF) | ((value & 0x0F) << 8));
                break;
            case Registers.UDRn:
                Transmit(value);
                break;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_USART: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            ucsrA = UCSRA_UDRE; /* TX data register empty on reset */
            ucsrB = 0;
            ucsrC = 0x06;       /* 8-bit, 1 stop, no parity */
            ubrr  = 0;
            rxQueue.Clear();
            IRQ_RX.Unset();
            IRQ_TX.Unset();
        }

        public void WriteChar(byte value)
        {
            rxQueue.Enqueue(value);
            ucsrA |= UCSRA_RXC;
            if((ucsrB & UCSRB_RXCIE) != 0)
            {
                IRQ_RX.Set();
                IRQ_RX.Unset();
            }
        }

        public event Action<byte> CharReceived;

        public uint BaudRate
        {
            get
            {
                if(ubrr == 0) return 9600;
                return (uint)(baseFrequency / (16UL * ((ulong)ubrr + 1)));
            }
        }

        public Bits StopBits => ((ucsrC >> 3) & 1) != 0 ? Bits.Two : Bits.One;

        public Parity ParityBit =>
            (ucsrC & 0x30) == 0x00 ? Parity.None :
            (ucsrC & 0x30) == 0x20 ? Parity.Even : Parity.Odd;

        public long Size => 0x07;

        public GPIO IRQ_RX { get; }
        public GPIO IRQ_TX { get; }

        private void Transmit(byte value)
        {
            CharReceived?.Invoke(value);
            ucsrA |= UCSRA_TXC | UCSRA_UDRE;
            if((ucsrB & UCSRB_TXCIE) != 0)
            {
                IRQ_TX.Set();
                IRQ_TX.Unset();
            }
            if((ucsrB & UCSRB_UDRIE) != 0)
            {
                IRQ_TX.Set();
                IRQ_TX.Unset();
            }
        }

        private byte BuildUCSRA()
        {
            byte st = ucsrA;
            if(rxQueue.Count == 0)
            {
                st &= unchecked((byte)~UCSRA_RXC);
            }
            return st;
        }

        private byte ReadRX()
        {
            if(rxQueue.Count == 0) return 0;
            var data = rxQueue.Dequeue();
            if(rxQueue.Count == 0)
            {
                ucsrA &= unchecked((byte)~UCSRA_RXC);
            }
            return data;
        }

        private enum Registers : long
        {
            UCSRnA = 0x00,
            UCSRnB = 0x01,
            UCSRnC = 0x02,
            UBRRnL = 0x04,
            UBRRnH = 0x05,
            UDRn   = 0x06,
        }

        private const byte UCSRA_UDRE = (1 << 5);
        private const byte UCSRA_TXC  = (1 << 6);
        private const byte UCSRA_RXC  = (byte)(1 << 7);

        private const byte UCSRB_UDRIE = (1 << 5);
        private const byte UCSRB_TXCIE = (1 << 6);
        private const byte UCSRB_RXCIE = (byte)(1 << 7);

        private byte   ucsrA;
        private byte   ucsrB;
        private byte   ucsrC;
        private ushort ubrr;

        private readonly long baseFrequency;
        private readonly Queue<byte> rxQueue;
    }
}
