//
// PIC16F USART peripheral.
// Mapped into the file register space via the data-memory bus.
// Simplified model; key registers:
//   0x00  RCSTA — receive status/control
//   0x01  TXSTA — transmit status/control
//   0x02  TXREG — transmit data register
//   0x03  RCREG — receive data register (read-only; FIFO depth 2)
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class PIC16_USART : IUART, IBytePeripheral, IKnownSize
    {
        public PIC16_USART(IMachine machine, long frequency = 4_000_000)
        {
            this.frequency = frequency;
            IRQ_TX = new GPIO();
            IRQ_RX = new GPIO();
            rxQueue = new Queue<byte>();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
            case 0: return rcsta;
            case 1: return txsta;
            case 2: return 0; /* TXREG not readable */
            case 3:
                if(rxQueue.Count > 0)
                {
                    var v = rxQueue.Dequeue();
                    if(rxQueue.Count == 0)
                        rcsta &= unchecked((byte)~RCSTA_RCIF);
                    return v;
                }
                return 0;
            default:
                this.Log(LogLevel.Warning, "PIC16_USART: Unknown read 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
            case 0: rcsta = value; break;
            case 1: txsta = value; break;
            case 2: Transmit(value); break;
            default: this.Log(LogLevel.Warning, "PIC16_USART: Unknown write 0x{0:X}@0x{1:X}", value, offset); break;
            }
        }

        public void Reset()
        {
            rcsta = 0; txsta = TXSTA_TXEN;
            rxQueue.Clear();
            IRQ_TX.Unset(); IRQ_RX.Unset();
        }

        public void WriteChar(byte value)
        {
            rxQueue.Enqueue(value);
            rcsta |= RCSTA_RCIF;
            if((rcsta & RCSTA_CREN) != 0) { IRQ_RX.Set(); IRQ_RX.Unset(); }
        }

        public event Action<byte> CharReceived;
        public uint BaudRate => (uint)(frequency / 64);  /* simplified */
        public Bits StopBits => Bits.One;
        public Parity ParityBit => Parity.None;
        public long Size => 0x04;
        public GPIO IRQ_TX { get; }
        public GPIO IRQ_RX { get; }

        private void Transmit(byte value)
        {
            CharReceived?.Invoke(value);
            if((txsta & TXSTA_TXIE) != 0) { IRQ_TX.Set(); IRQ_TX.Unset(); }
        }

        private const byte RCSTA_RCIF = (1 << 5);
        private const byte RCSTA_CREN = (1 << 4);
        private const byte TXSTA_TXEN = (1 << 5);
        private const byte TXSTA_TXIE = (1 << 7);

        private byte rcsta, txsta;
        private readonly long frequency;
        private readonly Queue<byte> rxQueue;
    }
}
