//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 DMA — 12-channel DMA controller.
//
// Per-channel register map (32-bit, stride = 0x40, channels 0-11):
//   0x00  CH_READ_ADDR       — Read address pointer
//   0x04  CH_WRITE_ADDR      — Write address pointer
//   0x08  CH_TRANS_COUNT      — Transfer count
//   0x0C  CH_CTRL_TRIG        — Control / trigger (EN, DATA_SIZE, INCR_READ, INCR_WRITE, 
//                                RING_SIZE, RING_SEL, CHAIN_TO, TREQ_SEL, IRQ_QUIET, BUSY, etc.)
//   0x10  CH_AL1_CTRL         — Alias 1: CTRL
//   0x30  CH_DBG_CTDREQ       — Debug: DREQ counter
//   0x34  CH_DBG_TCR          — Debug: transfer count reload
//
// Global registers at 0x400+:
//   0x400  INTR               — Raw interrupt status
//   0x404  INTE0              — IRQ0 enable
//   0x408  INTF0              — IRQ0 force
//   0x40C  INTS0              — IRQ0 status (masked)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.DMA
{
    public class RP2040_DMA : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_DMA(IMachine machine)
        {
            this.machine = machine;
            IRQ0 = new GPIO();
            IRQ1 = new GPIO();
            channels = new DmaChannel[ChannelCount];
            for(int i = 0; i < ChannelCount; i++)
                channels[i] = new DmaChannel();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            if(offset >= 0x400)
                return ReadGlobal(offset - 0x400);

            int ch = (int)(offset / ChannelStride);
            if(ch >= ChannelCount)
            {
                this.Log(LogLevel.Warning,
                         "RP2040_DMA: Read from invalid channel {0}", ch);
                return 0;
            }

            var c = channels[ch];
            switch((ChannelRegisters)(offset % ChannelStride))
            {
            case ChannelRegisters.READ_ADDR:    return c.readAddr;
            case ChannelRegisters.WRITE_ADDR:   return c.writeAddr;
            case ChannelRegisters.TRANS_COUNT:   return c.transCount;
            case ChannelRegisters.CTRL_TRIG:     return c.ctrl;
            case ChannelRegisters.AL1_CTRL:      return c.ctrl;
            case ChannelRegisters.DBG_CTDREQ:    return 0;
            case ChannelRegisters.DBG_TCR:       return c.transCountReload;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_DMA: Read from unknown channel offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(offset >= 0x400)
            {
                WriteGlobal(offset - 0x400, value);
                return;
            }

            int ch = (int)(offset / ChannelStride);
            if(ch >= ChannelCount)
            {
                this.Log(LogLevel.Warning,
                         "RP2040_DMA: Write to invalid channel {0}", ch);
                return;
            }

            var c = channels[ch];
            switch((ChannelRegisters)(offset % ChannelStride))
            {
            case ChannelRegisters.READ_ADDR:
                c.readAddr = value;
                break;
            case ChannelRegisters.WRITE_ADDR:
                c.writeAddr = value;
                break;
            case ChannelRegisters.TRANS_COUNT:
                c.transCount = value;
                c.transCountReload = value;
                break;
            case ChannelRegisters.CTRL_TRIG:
                c.ctrl = value;
                if((value & CTRL_EN) != 0)
                    TriggerChannel(ch);
                break;
            case ChannelRegisters.AL1_CTRL:
                c.ctrl = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_DMA: Write 0x{0:X8} to unknown channel offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            rawIntr = 0;
            inte0   = 0;
            intf0   = 0;
            inte1   = 0;
            intf1   = 0;
            for(int i = 0; i < ChannelCount; i++)
            {
                channels[i].readAddr  = 0;
                channels[i].writeAddr = 0;
                channels[i].transCount = 0;
                channels[i].transCountReload = 0;
                channels[i].ctrl = 0;
            }
            IRQ0.Unset();
            IRQ1.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ0 { get; }
        public GPIO IRQ1 { get; }

        private void TriggerChannel(int ch)
        {
            var c = channels[ch];
            int dataSize = (int)((c.ctrl >> 2) & 0x03); /* DATA_SIZE: 0=byte, 1=hw, 2=word */
            bool incrRead  = (c.ctrl & CTRL_INCR_READ) != 0;
            bool incrWrite = (c.ctrl & CTRL_INCR_WRITE) != 0;
            int byteWidth = 1 << dataSize;

            uint count = c.transCount;
            uint rAddr = c.readAddr;
            uint wAddr = c.writeAddr;

            for(uint i = 0; i < count; i++)
            {
                /* Model: copy via sysbus */
                uint data = 0;
                switch(byteWidth)
                {
                case 1:
                    data = machine.SystemBus.ReadByte(rAddr);
                    machine.SystemBus.WriteByte(wAddr, (byte)data);
                    break;
                case 2:
                    data = machine.SystemBus.ReadWord(rAddr);
                    machine.SystemBus.WriteWord(wAddr, (ushort)data);
                    break;
                default:
                    data = machine.SystemBus.ReadDoubleWord(rAddr);
                    machine.SystemBus.WriteDoubleWord(wAddr, data);
                    break;
                }

                if(incrRead)  rAddr += (uint)byteWidth;
                if(incrWrite) wAddr += (uint)byteWidth;
            }

            c.readAddr  = rAddr;
            c.writeAddr = wAddr;
            c.transCount = 0;
            c.ctrl &= ~CTRL_EN; /* clear enable — transfer done */

            rawIntr |= (1u << ch);
            UpdateIRQ();
        }

        private uint ReadGlobal(long off)
        {
            switch((GlobalRegisters)off)
            {
            case GlobalRegisters.INTR:  return rawIntr;
            case GlobalRegisters.INTE0: return inte0;
            case GlobalRegisters.INTF0: return intf0;
            case GlobalRegisters.INTS0: return (rawIntr | intf0) & inte0;
            case GlobalRegisters.INTE1: return inte1;
            case GlobalRegisters.INTF1: return intf1;
            case GlobalRegisters.INTS1: return (rawIntr | intf1) & inte1;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_DMA: Read from unknown global offset 0x{0:X}", off + 0x400);
                return 0;
            }
        }

        private void WriteGlobal(long off, uint value)
        {
            switch((GlobalRegisters)off)
            {
            case GlobalRegisters.INTR:
                rawIntr &= ~value;
                UpdateIRQ();
                break;
            case GlobalRegisters.INTE0:
                inte0 = value & ChannelMask;
                UpdateIRQ();
                break;
            case GlobalRegisters.INTF0:
                intf0 = value & ChannelMask;
                UpdateIRQ();
                break;
            case GlobalRegisters.INTE1:
                inte1 = value & ChannelMask;
                UpdateIRQ();
                break;
            case GlobalRegisters.INTF1:
                intf1 = value & ChannelMask;
                UpdateIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_DMA: Write 0x{0:X8} to unknown global offset 0x{1:X}", value, off + 0x400);
                break;
            }
        }

        private void UpdateIRQ()
        {
            if(((rawIntr | intf0) & inte0) != 0)
                IRQ0.Set();
            else
                IRQ0.Unset();

            if(((rawIntr | intf1) & inte1) != 0)
                IRQ1.Set();
            else
                IRQ1.Unset();
        }

        private class DmaChannel
        {
            public uint readAddr, writeAddr, transCount, transCountReload, ctrl;
        }

        private enum ChannelRegisters : long
        {
            READ_ADDR   = 0x00,
            WRITE_ADDR  = 0x04,
            TRANS_COUNT = 0x08,
            CTRL_TRIG   = 0x0C,
            AL1_CTRL    = 0x10,
            DBG_CTDREQ  = 0x30,
            DBG_TCR     = 0x34,
        }

        private enum GlobalRegisters : long
        {
            INTR  = 0x00,
            INTE0 = 0x04,
            INTF0 = 0x08,
            INTS0 = 0x0C,
            INTE1 = 0x14,
            INTF1 = 0x18,
            INTS1 = 0x1C,
        }

        private const int ChannelCount  = 12;
        private const int ChannelStride = 0x40;
        private const uint ChannelMask  = 0xFFF;
        private const uint CTRL_EN         = (1u << 0);
        private const uint CTRL_INCR_READ  = (1u << 4);
        private const uint CTRL_INCR_WRITE = (1u << 5);

        private uint rawIntr, inte0, intf0, inte1, intf1;
        private readonly DmaChannel[] channels;
        private readonly IMachine machine;
    }
}
