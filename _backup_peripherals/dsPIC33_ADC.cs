//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC33 12-bit ADC peripheral (ADC1 module).
//
// Register map (word-wide):
//   0x00  AD1CON1  — ADC Control 1 (ADON, ADSIDL, FORM, SSRC, ASAM, SAMP, DONE)
//   0x02  AD1CON2  — ADC Control 2 (VCFG, CSCNA, BUFS, SMPI, BUFM, ALTS)
//   0x04  AD1CON3  — ADC Control 3 (ADRC, SAMC, ADCS)
//   0x06  AD1CHS   — ADC Input Channel Select (CH0NA, CH0SA)
//   0x08  AD1BUFL  — ADC Result Buffer Low (first word of 16-word buffer)
//   0x0A  AD1BUFH  — ADC Result Buffer High
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class dsPIC33_ADC : IWordPeripheral, IKnownSize
    {
        public dsPIC33_ADC(IMachine machine, int channelCount = 16)
        {
            this.channelCount = channelCount;
            channelValues = new ushort[channelCount];
            resultBuffer = new ushort[16];
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.AD1CON1: return con1;
            case Registers.AD1CON2: return con2;
            case Registers.AD1CON3: return con3;
            case Registers.AD1CHS:  return chs;
            case Registers.AD1BUFL: return resultBuffer[bufIndex & 0x0F];
            case Registers.AD1BUFH: return 0;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_ADC: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.AD1CON1:
                con1 = value;
                if((value & CON1_SAMP) != 0 && (value & CON1_ADON) != 0)
                {
                    PerformConversion();
                }
                break;
            case Registers.AD1CON2:
                con2 = value;
                break;
            case Registers.AD1CON3:
                con3 = value;
                break;
            case Registers.AD1CHS:
                chs = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_ADC: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con1 = 0;
            con2 = 0;
            con3 = 0;
            chs  = 0;
            bufIndex = 0;
            for(int i = 0; i < channelCount; i++) channelValues[i] = 0;
            for(int i = 0; i < 16; i++) resultBuffer[i] = 0;
            IRQ.Unset();
        }

        /// <summary>Set a channel value (0–4095 for 12-bit).</summary>
        public void SetChannelValue(int channel, ushort value)
        {
            if(channel >= 0 && channel < channelCount)
                channelValues[channel] = (ushort)(value & 0xFFF);
        }

        public long Size => 0x0C;
        public GPIO IRQ { get; }

        private void PerformConversion()
        {
            int ch = chs & 0x1F; /* CH0SA4:0 */
            ushort result = (ch < channelCount) ? channelValues[ch] : (ushort)0;

            /* FORM bits: 00=integer, 01=signed integer, 10=fractional, 11=signed frac */
            int form = (con1 >> 8) & 0x03;
            if(form == 2 || form == 3)
                result = (ushort)((result << 4) & 0xFFF0);

            resultBuffer[bufIndex & 0x0F] = result;
            bufIndex++;

            con1 |= CON1_DONE;
            con1 &= unchecked((ushort)~CON1_SAMP);

            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            AD1CON1 = 0x00,
            AD1CON2 = 0x02,
            AD1CON3 = 0x04,
            AD1CHS  = 0x06,
            AD1BUFL = 0x08,
            AD1BUFH = 0x0A,
        }

        private const ushort CON1_DONE = (1 << 0);
        private const ushort CON1_SAMP = (1 << 1);
        private const ushort CON1_ADON = (ushort)(1 << 15);

        private ushort con1;
        private ushort con2;
        private ushort con3;
        private ushort chs;
        private int bufIndex;

        private readonly int channelCount;
        private readonly ushort[] channelValues;
        private readonly ushort[] resultBuffer;
    }
}
