//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// TMS320C28x 12-bit ADC peripheral (ADC module).
//
// Register map (16-bit word addresses, byte offsets — key registers):
//   0x00  ADCCTL1      — ADC Control 1 (ADCPWDN, ADCBGPWD, ADCREFPWD, ADCENABLE, ADCREFSEL)
//   0x02  ADCCTL2      — ADC Control 2 (CLKDIV, ADCNONOVERLAP)
//   0x10  ADCSOC0CTL   — SOC0 control (CHSEL, ACQPS, TRIGSEL)
//   0x12  ADCSOC1CTL   — SOC1 control
//   0x14  ADCSOC2CTL   — SOC2 control
//   0x16  ADCSOC3CTL   — SOC3 control
//   0x18  ADCSOC4CTL   — SOC4 control
//   0x1A  ADCSOC5CTL   — SOC5 control
//   0x1C  ADCSOC6CTL   — SOC6 control
//   0x1E  ADCSOC7CTL   — SOC7 control
//   0x50  ADCINTFLG    — Interrupt flag register
//   0x52  ADCINTFLGCLR — Interrupt flag clear
//   0x54  ADCINTOVF    — Interrupt overflow flag
//   0x56  ADCINTOVFCLR — Interrupt overflow clear
//   0x58  ADCINTSEL1N2 — Interrupt source select (ADCINT1/ADCINT2)
//   0x80  ADCRESULT0   — SOC0 result
//   0x82  ADCRESULT1   — SOC1 result
//   ... (up to ADCRESULT15 at 0x9E)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class C2000_ADC12 : IWordPeripheral, IKnownSize
    {
        public C2000_ADC12(IMachine machine, int channelCount = 16)
        {
            this.channelCount = channelCount;
            channelValues = new ushort[channelCount];
            socCtl = new ushort[MaxSOC];
            results = new ushort[MaxSOC];
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            if(offset >= 0x80 && offset < 0x80 + MaxSOC * 2)
            {
                int idx = (int)(offset - 0x80) / 2;
                return results[idx];
            }

            switch((Registers)offset)
            {
            case Registers.ADCCTL1:      return adcctl1;
            case Registers.ADCCTL2:      return adcctl2;
            case Registers.ADCSOC0CTL:   return socCtl[0];
            case Registers.ADCSOC1CTL:   return socCtl[1];
            case Registers.ADCSOC2CTL:   return socCtl[2];
            case Registers.ADCSOC3CTL:   return socCtl[3];
            case Registers.ADCSOC4CTL:   return socCtl[4];
            case Registers.ADCSOC5CTL:   return socCtl[5];
            case Registers.ADCSOC6CTL:   return socCtl[6];
            case Registers.ADCSOC7CTL:   return socCtl[7];
            case Registers.ADCINTFLG:    return intflg;
            case Registers.ADCINTOVF:    return intovf;
            case Registers.ADCINTSEL1N2: return intsel;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_ADC12: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.ADCCTL1:
                adcctl1 = value;
                break;
            case Registers.ADCCTL2:
                adcctl2 = value;
                break;
            case Registers.ADCSOC0CTL: socCtl[0] = value; TriggerSOC(0); break;
            case Registers.ADCSOC1CTL: socCtl[1] = value; TriggerSOC(1); break;
            case Registers.ADCSOC2CTL: socCtl[2] = value; TriggerSOC(2); break;
            case Registers.ADCSOC3CTL: socCtl[3] = value; TriggerSOC(3); break;
            case Registers.ADCSOC4CTL: socCtl[4] = value; TriggerSOC(4); break;
            case Registers.ADCSOC5CTL: socCtl[5] = value; TriggerSOC(5); break;
            case Registers.ADCSOC6CTL: socCtl[6] = value; TriggerSOC(6); break;
            case Registers.ADCSOC7CTL: socCtl[7] = value; TriggerSOC(7); break;
            case Registers.ADCINTFLGCLR:
                intflg &= (ushort)~value;
                UpdateIRQ();
                break;
            case Registers.ADCINTOVFCLR:
                intovf &= (ushort)~value;
                break;
            case Registers.ADCINTSEL1N2:
                intsel = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_ADC12: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            adcctl1 = 0;
            adcctl2 = 0;
            intflg  = 0;
            intovf  = 0;
            intsel  = 0;
            for(int i = 0; i < MaxSOC; i++)
            {
                socCtl[i]  = 0;
                results[i] = 0;
            }
            for(int i = 0; i < channelCount; i++)
                channelValues[i] = 0;
            IRQ.Unset();
        }

        /// <summary>Set a channel value (0–4095 for 12-bit).</summary>
        public void SetChannelValue(int channel, ushort value)
        {
            if(channel >= 0 && channel < channelCount)
                channelValues[channel] = (ushort)(value & 0xFFF);
        }

        public long Size => 0xA0;
        public GPIO IRQ { get; }

        private void TriggerSOC(int soc)
        {
            if((adcctl1 & CTL1_ADCENABLE) == 0) return;

            int ch = (socCtl[soc] >> 6) & 0x0F; /* CHSEL bits */
            ushort val = (ch < channelCount) ? channelValues[ch] : (ushort)0;
            results[soc] = (ushort)(val << 4); /* left-aligned in 16-bit result register */

            intflg |= (ushort)(1 << 0); /* ADCINT1 */
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            if(intflg != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            ADCCTL1      = 0x00,
            ADCCTL2      = 0x02,
            ADCSOC0CTL   = 0x10,
            ADCSOC1CTL   = 0x12,
            ADCSOC2CTL   = 0x14,
            ADCSOC3CTL   = 0x16,
            ADCSOC4CTL   = 0x18,
            ADCSOC5CTL   = 0x1A,
            ADCSOC6CTL   = 0x1C,
            ADCSOC7CTL   = 0x1E,
            ADCINTFLG    = 0x50,
            ADCINTFLGCLR = 0x52,
            ADCINTOVF    = 0x54,
            ADCINTOVFCLR = 0x56,
            ADCINTSEL1N2 = 0x58,
        }

        private const ushort CTL1_ADCENABLE = (1 << 14);
        private const int MaxSOC = 16;

        private ushort adcctl1, adcctl2;
        private ushort intflg, intovf, intsel;
        private readonly int channelCount;
        private readonly ushort[] channelValues;
        private readonly ushort[] socCtl;
        private readonly ushort[] results;
    }
}
