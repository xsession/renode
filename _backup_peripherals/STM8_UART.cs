//
// STM8 UART1 peripheral.
// Register map (byte offsets from peripheral base):
//   0x00  UART_SR   — status register
//   0x01  UART_DR   — data register (read=RX, write=TX)
//   0x02  UART_BRR1 — baud rate register 1
//   0x03  UART_BRR2 — baud rate register 2
//   0x04  UART_CR1  — control register 1
//   0x05  UART_CR2  — control register 2
//   0x06  UART_CR3  — control register 3
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class STM8_UART : IUART, IBytePeripheral, IKnownSize
    {
        public STM8_UART(IMachine machine, long baseFrequency = 2_000_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ = new GPIO();
            rxQueue = new Queue<byte>();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
            case 0x00: return BuildSR();
            case 0x01: return ReadDR();
            case 0x02: return brr1;
            case 0x03: return brr2;
            case 0x04: return cr1;
            case 0x05: return cr2;
            case 0x06: return cr3;
            default:
                this.Log(LogLevel.Warning, "STM8_UART: Unknown read 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
            case 0x00: break; /* SR: some bits are read-only / w1c */
            case 0x01: Transmit(value); break;
            case 0x02: brr1 = value; break;
            case 0x03: brr2 = value; break;
            case 0x04: cr1 = value; break;
            case 0x05: cr2 = value; break;
            case 0x06: cr3 = value; break;
            default: this.Log(LogLevel.Warning, "STM8_UART: Unknown write 0x{0:X}@0x{1:X}", value, offset); break;
            }
        }

        public void Reset()
        {
            sr = SR_TXE | SR_TC; /* TX empty + complete */
            brr1 = 0; brr2 = 0; cr1 = 0; cr2 = 0; cr3 = 0;
            rxQueue.Clear();
            IRQ.Unset();
        }

        public void WriteChar(byte value)
        {
            rxQueue.Enqueue(value);
            sr |= SR_RXNE;
            if((cr2 & CR2_RIEN) != 0) { IRQ.Set(); IRQ.Unset(); }
        }

        public event Action<byte> CharReceived;
        public uint BaudRate => baseFrequency / 16;  /* simplified */
        public Bits StopBits => Bits.One;
        public Parity ParityBit => Parity.None;
        public long Size => 0x07;
        public GPIO IRQ { get; }

        private byte BuildSR()
        {
            if(rxQueue.Count == 0) sr &= unchecked((byte)~SR_RXNE);
            else sr |= SR_RXNE;
            return sr;
        }

        private byte ReadDR()
        {
            if(rxQueue.Count == 0) return 0;
            var v = rxQueue.Dequeue();
            if(rxQueue.Count == 0) sr &= unchecked((byte)~SR_RXNE);
            return v;
        }

        private void Transmit(byte value)
        {
            CharReceived?.Invoke(value);
            sr |= SR_TXE | SR_TC;
        }

        private const byte SR_RXNE = (1 << 5);
        private const byte SR_TC   = (1 << 6);
        private const byte SR_TXE  = (byte)(1 << 7);
        private const byte CR2_RIEN = (1 << 5);

        private byte sr, brr1, brr2, cr1, cr2, cr3;
        private readonly long baseFrequency;
        private readonly Queue<byte> rxQueue;
    }
}
