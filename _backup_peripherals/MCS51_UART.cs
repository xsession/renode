//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// 8051 (MCS-51) UART peripheral — SCON / SBUF serial port model.
//
// Register map (byte-wide, mapped to SFR space):
//   0x00  SCON  — Serial Control (SM0/SM1, REN, TI, RI, etc.)
//   0x01  SBUF  — Serial Buffer (read = RX, write = TX)
//   0x02  PCON  — Power Control (SMOD baud rate doubler)
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.UART
{
    public class MCS51_UART : IUART, IBytePeripheral, IKnownSize
    {
        public MCS51_UART(IMachine machine, long baseFrequency = 11_059_200)
        {
            this.baseFrequency = baseFrequency;
            IRQ = new GPIO();
            rxQueue = new Queue<byte>();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SCON: return scon;
            case Registers.SBUF: return ReadRX();
            case Registers.PCON: return pcon;
            default:
                this.Log(LogLevel.Warning,
                         "MCS51_UART: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.SCON:
                scon = value;
                break;
            case Registers.SBUF:
                Transmit(value);
                break;
            case Registers.PCON:
                pcon = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "MCS51_UART: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            scon = 0;
            pcon = 0;
            rxQueue.Clear();
            IRQ.Unset();
        }

        public void WriteChar(byte value)
        {
            rxQueue.Enqueue(value);
            scon |= SCON_RI;
            UpdateIRQ();
        }

        public event Action<byte> CharReceived;

        public uint BaudRate => 9600;
        public Bits StopBits => Bits.One;
        public Parity ParityBit => Parity.None;

        public long Size => 0x03;

        public GPIO IRQ { get; }

        private void Transmit(byte value)
        {
            CharReceived?.Invoke(value);
            scon |= SCON_TI;
            UpdateIRQ();
        }

        private byte ReadRX()
        {
            if(rxQueue.Count == 0) return 0;
            var data = rxQueue.Dequeue();
            if(rxQueue.Count == 0)
            {
                scon &= unchecked((byte)~SCON_RI);
            }
            return data;
        }

        private void UpdateIRQ()
        {
            if((scon & (SCON_TI | SCON_RI)) != 0)
            {
                IRQ.Set();
                IRQ.Unset();
            }
        }

        private enum Registers : long
        {
            SCON = 0x00,
            SBUF = 0x01,
            PCON = 0x02,
        }

        private const byte SCON_RI  = (1 << 0);
        private const byte SCON_TI  = (1 << 1);
        private const byte SCON_REN = (1 << 4);

        private byte scon;
        private byte pcon;

        private readonly long baseFrequency;
        private readonly Queue<byte> rxQueue;
    }
}
