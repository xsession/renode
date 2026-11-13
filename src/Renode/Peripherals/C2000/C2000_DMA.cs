//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x DMA Controller (6 channels).
//
// Register map per channel (0x20 each):
//   0x00  MODE         — Channel mode control
//   0x02  CONTROL      — Channel control (run/halt/perintfrc)
//   0x04  BURST_SIZE   — Burst size (number of words - 1)
//   0x06  BURST_COUNT  — Burst counter (read-only)
//   0x08  SRC_BEG_ADDR_L  — Source begin address (low 16)
//   0x0A  SRC_BEG_ADDR_H  — Source begin address (high 16)
//   0x0C  SRC_ADDR_L   — Current source address (low 16)
//   0x0E  SRC_ADDR_H   — Current source address (high 16)
//   0x10  DST_BEG_ADDR_L
//   0x12  DST_BEG_ADDR_H
//   0x14  DST_ADDR_L
//   0x16  DST_ADDR_H
//   0x18  TRANSFER_SIZE — Transfer size (number of bursts - 1)
//   0x1A  TRANSFER_COUNT — Transfer counter (read-only)
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.DMA
{
    public class C2000_DMA : IWordPeripheral, IKnownSize
    {
        public C2000_DMA(IMachine machine)
        {
            this.machine = machine;
            sysbus = machine.GetSystemBus(this);
            channels = new DmaChannel[NUM_CHANNELS];
            for(int i = 0; i < NUM_CHANNELS; i++)
            {
                channels[i] = new DmaChannel();
            }
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            if(offset < CHANNEL_BASE)
            {
                switch(offset)
                {
                case 0x00: return dmactrl;
                case 0x02: return debugctrl;
                case 0x06: return priorityctrl;
                case 0x08: return prioritystat;
                default:
                    this.Log(LogLevel.Warning, "C2000_DMA: Read from global offset 0x{0:X}", offset);
                    return 0;
                }
            }

            int ch = (int)((offset - CHANNEL_BASE) / CHANNEL_STRIDE);
            int reg = (int)((offset - CHANNEL_BASE) % CHANNEL_STRIDE);
            if(ch >= NUM_CHANNELS) return 0;

            var c = channels[ch];
            switch(reg)
            {
            case 0x00: return c.mode;
            case 0x02: return c.control;
            case 0x04: return c.burstSize;
            case 0x06: return c.burstCount;
            case 0x08: return (ushort)(c.srcBeginAddr & 0xFFFF);
            case 0x0A: return (ushort)((c.srcBeginAddr >> 16) & 0xFFFF);
            case 0x0C: return (ushort)(c.srcAddr & 0xFFFF);
            case 0x0E: return (ushort)((c.srcAddr >> 16) & 0xFFFF);
            case 0x10: return (ushort)(c.dstBeginAddr & 0xFFFF);
            case 0x12: return (ushort)((c.dstBeginAddr >> 16) & 0xFFFF);
            case 0x14: return (ushort)(c.dstAddr & 0xFFFF);
            case 0x16: return (ushort)((c.dstAddr >> 16) & 0xFFFF);
            case 0x18: return c.transferSize;
            case 0x1A: return c.transferCount;
            default:
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            if(offset < CHANNEL_BASE)
            {
                switch(offset)
                {
                case 0x00: dmactrl = value; break;
                case 0x02: debugctrl = value; break;
                case 0x06: priorityctrl = value; break;
                default:
                    this.Log(LogLevel.Warning, "C2000_DMA: Write 0x{0:X} to global offset 0x{1:X}", value, offset);
                    break;
                }
                return;
            }

            int ch = (int)((offset - CHANNEL_BASE) / CHANNEL_STRIDE);
            int reg = (int)((offset - CHANNEL_BASE) % CHANNEL_STRIDE);
            if(ch >= NUM_CHANNELS) return;

            var c = channels[ch];
            switch(reg)
            {
            case 0x00: c.mode = value; break;
            case 0x02:
                c.control = value;
                if((value & CTRL_RUN) != 0)
                    c.running = true;
                if((value & CTRL_HALT) != 0)
                    c.running = false;
                if((value & CTRL_PERINTFRC) != 0)
                    PerformTransfer(ch);
                break;
            case 0x04: c.burstSize = value; break;
            case 0x08: c.srcBeginAddr = (c.srcBeginAddr & 0xFFFF0000u) | value; break;
            case 0x0A: c.srcBeginAddr = (c.srcBeginAddr & 0x0000FFFFu) | ((uint)value << 16); break;
            case 0x10: c.dstBeginAddr = (c.dstBeginAddr & 0xFFFF0000u) | value; break;
            case 0x12: c.dstBeginAddr = (c.dstBeginAddr & 0x0000FFFFu) | ((uint)value << 16); break;
            case 0x18: c.transferSize = value; break;
            default:
                break;
            }
        }

        public void TriggerChannel(int ch)
        {
            if(ch >= 0 && ch < NUM_CHANNELS && channels[ch].running)
            {
                PerformTransfer(ch);
            }
        }

        public void Reset()
        {
            dmactrl = 0;
            debugctrl = 0;
            priorityctrl = 0;
            prioritystat = 0;
            for(int i = 0; i < NUM_CHANNELS; i++)
                channels[i].Reset();
            IRQ.Unset();
        }

        public long Size => 0x1C0;

        public GPIO IRQ { get; }

        private void PerformTransfer(int ch)
        {
            var c = channels[ch];
            int burstWords = c.burstSize + 1;
            c.srcAddr = c.srcBeginAddr;
            c.dstAddr = c.dstBeginAddr;

            for(int i = 0; i < burstWords; i++)
            {
                ushort data = sysbus.ReadWord(c.srcAddr);
                sysbus.WriteWord(c.dstAddr, data);
                c.srcAddr += 2;
                c.dstAddr += 2;
            }

            c.burstCount = 0;
            c.transferCount = (ushort)(c.transferCount > 0 ? c.transferCount - 1 : 0);

            bool oneShot = (c.mode & MODE_ONESHOT) != 0;
            if(oneShot || c.transferCount == 0)
                c.running = false;

            if((c.mode & MODE_CHINTE) != 0)
            {
                IRQ.Set();
                IRQ.Unset();
            }
        }

        private class DmaChannel
        {
            public ushort mode;
            public ushort control;
            public ushort burstSize;
            public ushort burstCount;
            public uint srcBeginAddr;
            public uint srcAddr;
            public uint dstBeginAddr;
            public uint dstAddr;
            public ushort transferSize;
            public ushort transferCount;
            public bool running;

            public void Reset()
            {
                mode = control = burstSize = burstCount = transferSize = transferCount = 0;
                srcBeginAddr = srcAddr = dstBeginAddr = dstAddr = 0;
                running = false;
            }
        }

        private const int NUM_CHANNELS = 6;
        private const int CHANNEL_BASE = 0x20;
        private const int CHANNEL_STRIDE = 0x20;
        private const ushort CTRL_RUN = (1 << 0);
        private const ushort CTRL_HALT = (1 << 1);
        private const ushort CTRL_PERINTFRC = (1 << 2);
        private const ushort MODE_ONESHOT = (1 << 0);
        private const ushort MODE_CHINTE = (1 << 9);

        private ushort dmactrl;
        private ushort debugctrl;
        private ushort priorityctrl;
        private ushort prioritystat;
        private readonly DmaChannel[] channels;
        private readonly IMachine machine;
        private readonly IBusController sysbus;
    }
}
