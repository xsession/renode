//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC18 10-bit ADC peripheral (Enhanced ADC module).
//
// Register map (byte-wide):
//   0x00  ADCON0  — ADC Control 0 (CHS3:0, GO/DONE, ADON)
//   0x01  ADCON1  — ADC Control 1 (VCFG1:0, PCFG3:0)
//   0x02  ADCON2  — ADC Control 2 (ADFM, ACQT2:0, ADCS2:0)
//   0x03  ADRESL  — ADC Result Low
//   0x04  ADRESH  — ADC Result High
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class PIC18_ADC : IBytePeripheral, IKnownSize
    {
        public PIC18_ADC(IMachine machine, int channelCount = 13)
        {
            this.channelCount = channelCount;
            channelValues = new ushort[channelCount];
            IRQ = new GPIO();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.ADCON0: return adcon0;
            case Registers.ADCON1: return adcon1;
            case Registers.ADCON2: return adcon2;
            case Registers.ADRESL: return (byte)(adcResult & 0xFF);
            case Registers.ADRESH: return (byte)((adcResult >> 8) & 0x03);
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_ADC: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.ADCON0:
                adcon0 = value;
                if((value & ADCON0_GO) != 0 && (value & ADCON0_ADON) != 0)
                    PerformConversion();
                break;
            case Registers.ADCON1:
                adcon1 = value;
                break;
            case Registers.ADCON2:
                adcon2 = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_ADC: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            adcon0    = 0;
            adcon1    = 0x0F; /* All pins digital by default */
            adcon2    = 0;
            adcResult = 0;
            for(int i = 0; i < channelCount; i++)
                channelValues[i] = 0;
            IRQ.Unset();
        }

        public void SetChannelValue(int channel, ushort value)
        {
            if(channel >= 0 && channel < channelCount)
                channelValues[channel] = (ushort)(value & 0x3FF);
        }

        public long Size => 0x05;
        public GPIO IRQ { get; }

        private void PerformConversion()
        {
            int ch = (adcon0 >> 2) & 0x0F;
            adcResult = (ch < channelCount) ? channelValues[ch] : (ushort)0;

            /* ADFM: right-justified (1) or left-justified (0) */
            if((adcon2 & ADCON2_ADFM) == 0)
                adcResult = (ushort)((adcResult << 6) & 0xFFC0);

            adcon0 &= unchecked((byte)~ADCON0_GO);
            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            ADCON0 = 0x00,
            ADCON1 = 0x01,
            ADCON2 = 0x02,
            ADRESL = 0x03,
            ADRESH = 0x04,
        }

        private const byte ADCON0_ADON = (1 << 0);
        private const byte ADCON0_GO   = (1 << 1);
        private const byte ADCON2_ADFM = (byte)(1 << 7);

        private byte   adcon0;
        private byte   adcon1;
        private byte   adcon2;
        private ushort adcResult;

        private readonly int channelCount;
        private readonly ushort[] channelValues;
    }
}
