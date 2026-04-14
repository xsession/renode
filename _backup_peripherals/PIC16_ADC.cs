//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC16 10-bit ADC peripheral.
//
// Register map (byte-wide):
//   0x00  ADCON0  — ADC Control 0 (ADCS1:0, CHS2:0, GO/DONE, ADON)
//   0x01  ADCON1  — ADC Control 1 (ADFM, PCFG3:0)
//   0x02  ADRESL  — ADC Result Low
//   0x03  ADRESH  — ADC Result High
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class PIC16_ADC : IBytePeripheral, IKnownSize
    {
        public PIC16_ADC(IMachine machine, int channelCount = 8)
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
            case Registers.ADRESL: return (byte)(adcResult & 0xFF);
            case Registers.ADRESH: return (byte)((adcResult >> 8) & 0x03);
            default:
                this.Log(LogLevel.Warning,
                         "PIC16_ADC: Read from unknown offset 0x{0:X}", offset);
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
                {
                    PerformConversion();
                }
                break;
            case Registers.ADCON1:
                adcon1 = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC16_ADC: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            adcon0    = 0;
            adcon1    = 0;
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

        public long Size => 0x04;
        public GPIO IRQ { get; }

        private void PerformConversion()
        {
            int ch = (adcon0 >> 3) & 0x07; /* CHS2:0 */
            if(ch < channelCount)
                adcResult = channelValues[ch];
            else
                adcResult = 0;

            /* ADFM: right-justified (1) or left-justified (0) */
            if((adcon1 & ADCON1_ADFM) == 0)
            {
                adcResult = (ushort)((adcResult << 6) & 0xFFC0);
            }

            adcon0 &= unchecked((byte)~ADCON0_GO);
            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            ADCON0 = 0x00,
            ADCON1 = 0x01,
            ADRESL = 0x02,
            ADRESH = 0x03,
        }

        private const byte ADCON0_ADON = (1 << 0);
        private const byte ADCON0_GO   = (1 << 2);
        private const byte ADCON1_ADFM = (byte)(1 << 7);

        private byte   adcon0;
        private byte   adcon1;
        private ushort adcResult;

        private readonly int channelCount;
        private readonly ushort[] channelValues;
    }
}
