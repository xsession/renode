//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC33 Output Compare / PWM peripheral (OCx module).
//
// Register map (word-wide):
//   0x00  OCxCON1  — Output Compare Control 1 (OCSIDL, OCTSEL, OCM2:0)
//   0x02  OCxCON2  — Output Compare Control 2 (FLTMD, FLTOUT, FLTTRIEN, OCINV,
//                    DCB1:0, OC32, OCTRIG, TRIGMODE, OCFLT, SYNCSEL)
//   0x04  OCxRS    — Secondary Compare Value (period / duty)
//   0x06  OCxR     — Primary Compare Value
//   0x08  OCxTMR   — Timer Value
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class dsPIC33_PWM : IWordPeripheral, IKnownSize
    {
        public dsPIC33_PWM(IMachine machine, long baseFrequency = 70_000_000)
        {
            IRQ = new GPIO();
            PWMOutput = new GPIO();

            pwmTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                      this, "dsPIC33_PWM",
                                      limit: 0xFFFF,
                                      enabled: false,
                                      eventEnabled: true);
            pwmTimer.LimitReached += OnPeriodComplete;

            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.OCxCON1: return con1;
            case Registers.OCxCON2: return con2;
            case Registers.OCxRS:   return ocrs;
            case Registers.OCxR:    return ocr;
            case Registers.OCxTMR:  return (ushort)(pwmTimer.Value & 0xFFFF);
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_PWM: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.OCxCON1:
                con1 = value;
                ApplyMode();
                break;
            case Registers.OCxCON2:
                con2 = value;
                break;
            case Registers.OCxRS:
                ocrs = value;
                if(ocrs > 0) pwmTimer.Limit = ocrs;
                break;
            case Registers.OCxR:
                ocr = value;
                break;
            case Registers.OCxTMR:
                pwmTimer.Value = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_PWM: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con1 = 0;
            con2 = 0;
            ocrs = 0;
            ocr  = 0;
            pwmTimer.Enabled = false;
            pwmTimer.Value = 0;
            IRQ.Unset();
            PWMOutput.Unset();
        }

        public long Size => 0x0A;
        public GPIO IRQ { get; }
        public GPIO PWMOutput { get; }

        private void ApplyMode()
        {
            int ocm = con1 & 0x07; /* OCM2:0 */
            switch(ocm)
            {
            case 0: /* Disabled */
                pwmTimer.Enabled = false;
                break;
            case 5: /* Continuous pulse */
            case 6: /* PWM without fault */
            case 7: /* PWM with fault */
                pwmTimer.Enabled = true;
                break;
            default:
                /* Other modes (toggle, one-shot) — simplified */
                pwmTimer.Enabled = true;
                break;
            }
        }

        private void OnPeriodComplete()
        {
            PWMOutput.Set(true);
            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            OCxCON1 = 0x00,
            OCxCON2 = 0x02,
            OCxRS   = 0x04,
            OCxR    = 0x06,
            OCxTMR  = 0x08,
        }

        private ushort con1;
        private ushort con2;
        private ushort ocrs;
        private ushort ocr;

        private readonly LimitTimer pwmTimer;
    }
}
