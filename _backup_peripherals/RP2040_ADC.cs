//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 ADC — 12-bit SAR ADC with 5 channels (GPIO26-29 + temperature sensor).
//
// Register map (32-bit, byte offsets):
//   0x00  CS       — Control & status (EN, TS_EN, START_ONCE, READY, AINSEL)
//   0x04  RESULT   — Last conversion result (12-bit)
//   0x08  FCS      — FIFO control & status
//   0x0C  FIFO     — FIFO read
//   0x10  DIV      — Clock divider
//   0x14  INTR     — Raw interrupt status
//   0x18  INTE     — Interrupt enable
//   0x1C  INTF     — Interrupt force
//   0x20  INTS     — Interrupt status (after masking)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class RP2040_ADC : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_ADC(IMachine machine)
        {
            IRQ = new GPIO();
            channelValues = new ushort[ChannelCount];
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.CS:     return cs;
            case Registers.RESULT: return result;
            case Registers.FCS:    return fcs;
            case Registers.FIFO:   return result; /* simplified: return last result */
            case Registers.DIV:    return div;
            case Registers.INTR:   return rawIntr;
            case Registers.INTE:   return inte;
            case Registers.INTF:   return intf;
            case Registers.INTS:   return (rawIntr | intf) & inte;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_ADC: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.CS:
                cs = value & CS_WRITABLE;
                if((value & CS_START_ONCE) != 0)
                {
                    PerformConversion();
                }
                break;
            case Registers.FCS:
                fcs = value & FCS_WRITABLE;
                break;
            case Registers.DIV:
                div = value;
                break;
            case Registers.INTE:
                inte = value & 0x01;
                UpdateIRQ();
                break;
            case Registers.INTF:
                intf = value & 0x01;
                UpdateIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_ADC: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            cs       = CS_READY;
            result   = 0;
            fcs      = 0;
            div      = 0;
            rawIntr  = 0;
            inte     = 0;
            intf     = 0;
            for(int i = 0; i < ChannelCount; i++)
                channelValues[i] = 0;
            IRQ.Unset();
        }

        /// <summary>
        /// Set a channel value (0–4095). Channels 0–3 = GPIO26–29, channel 4 = temperature sensor.
        /// </summary>
        public void SetChannelValue(int channel, ushort value)
        {
            if(channel >= 0 && channel < ChannelCount)
                channelValues[channel] = (ushort)(value & 0xFFF);
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private void PerformConversion()
        {
            int ch = (int)((cs >> 12) & 0x07); /* AINSEL[2:0] */
            if(ch >= ChannelCount) ch = ChannelCount - 1;

            result = channelValues[ch];
            cs |= CS_READY;
            cs &= ~CS_START_ONCE;
            rawIntr |= 0x01; /* FIFO interrupt */
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            if(((rawIntr | intf) & inte) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            CS     = 0x00,
            RESULT = 0x04,
            FCS    = 0x08,
            FIFO   = 0x0C,
            DIV    = 0x10,
            INTR   = 0x14,
            INTE   = 0x18,
            INTF   = 0x1C,
            INTS   = 0x20,
        }

        private const uint CS_EN         = (1 << 0);
        private const uint CS_TS_EN      = (1 << 1);
        private const uint CS_START_ONCE = (1 << 2);
        private const uint CS_READY      = (1 << 8);
        private const uint CS_WRITABLE   = 0x0000F00F;
        private const uint FCS_WRITABLE  = 0x0F0F0F0F;
        private const int ChannelCount   = 5;

        private uint cs, result, fcs, div, rawIntr, inte, intf;
        private readonly ushort[] channelValues;
    }
}
