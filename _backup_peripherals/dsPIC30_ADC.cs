//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC30 10-bit ADC peripheral (ADC module).
//
// Register map (word-wide, dsPIC30F Family Reference Manual, Section 17):
//   0x00  ADCON1  — ADC Control 1 (ADON, ADSIDL, FORM, SSRC, ASAM, SAMP, DONE)
//   0x02  ADCON2  — ADC Control 2 (VCFG, CSCNA, BUFS, SMPI, BUFM, ALTS)
//   0x04  ADCON3  — ADC Control 3 (ADRC, SAMC, ADCS)
//   0x06  ADCHS   — ADC Input Channel Select (CH0NA, CH0SA)
//   0x08  ADPCFG  — ADC Port Configuration (1=digital, 0=analog)
//   0x0A  ADCSSL  — ADC Input Scan Select
//   0x10  ADCBUF0 — ADC Buffer 0 (first word of 16-word result buffer)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class dsPIC30_ADC : IWordPeripheral, IKnownSize
    {
        public dsPIC30_ADC(IMachine machine, int channelCount = 16)
        {
            this.channelCount = channelCount;
            channelValues = new ushort[channelCount];
            resultBuffer = new ushort[16];
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            /* Buffer reads: 0x10–0x2E (16 words) */
            if(offset >= 0x10 && offset < 0x10 + 16 * 2)
            {
                int idx = (int)(offset - 0x10) / 2;
                return resultBuffer[idx];
            }

            switch((Registers)offset)
            {
            case Registers.ADCON1: return con1;
            case Registers.ADCON2: return con2;
            case Registers.ADCON3: return con3;
            case Registers.ADCHS:  return chs;
            case Registers.ADPCFG: return pcfg;
            case Registers.ADCSSL: return cssl;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_ADC: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.ADCON1:
                con1 = value;
                if((value & CON1_SAMP) != 0 && (value & CON1_ADON) != 0)
                    PerformConversion();
                break;
            case Registers.ADCON2:
                con2 = value;
                break;
            case Registers.ADCON3:
                con3 = value;
                break;
            case Registers.ADCHS:
                chs = value;
                break;
            case Registers.ADPCFG:
                pcfg = value;
                break;
            case Registers.ADCSSL:
                cssl = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_ADC: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con1 = 0;
            con2 = 0;
            con3 = 0;
            chs  = 0;
            pcfg = 0;
            cssl = 0;
            bufIndex = 0;
            for(int i = 0; i < channelCount; i++) channelValues[i] = 0;
            for(int i = 0; i < 16; i++) resultBuffer[i] = 0;
            IRQ.Unset();
        }

        /// <summary>Set a channel value (0–1023 for 10-bit).</summary>
        public void SetChannelValue(int channel, ushort value)
        {
            if(channel >= 0 && channel < channelCount)
                channelValues[channel] = (ushort)(value & 0x3FF);
        }

        public long Size => 0x30;
        public GPIO IRQ { get; }

        private void PerformConversion()
        {
            int ch = chs & 0x0F; /* CH0SA3:0 */
            ushort result = (ch < channelCount) ? channelValues[ch] : (ushort)0;

            /* FORM bits: 00=integer, 01=signed int, 10=fractional, 11=signed frac */
            int form = (con1 >> 8) & 0x03;
            if(form == 2 || form == 3)
                result = (ushort)((result << 6) & 0xFFC0); /* 10-bit left-aligned to 16 bits */

            resultBuffer[bufIndex & 0x0F] = result;
            bufIndex++;

            con1 |= CON1_DONE;
            con1 &= unchecked((ushort)~CON1_SAMP);

            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            ADCON1 = 0x00,
            ADCON2 = 0x02,
            ADCON3 = 0x04,
            ADCHS  = 0x06,
            ADPCFG = 0x08,
            ADCSSL = 0x0A,
        }

        private const ushort CON1_ADON = (1 << 15);
        private const ushort CON1_SAMP = (1 << 1);
        private const ushort CON1_DONE = (1 << 0);

        private readonly int channelCount;
        private readonly ushort[] channelValues;
        private readonly ushort[] resultBuffer;
        private ushort con1, con2, con3, chs, pcfg, cssl;
        private int bufIndex;
    }
}
