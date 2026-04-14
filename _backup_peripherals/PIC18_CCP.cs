//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC18 CCP (Capture/Compare/PWM) peripheral.
//
// Register map (byte-wide):
//   0x00  CCPxCON  — CCP Control (DCxB1:0, CCPxM3:0)
//   0x01  CCPR1L   — Capture/Compare Low
//   0x02  CCPR1H   — Capture/Compare High
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class PIC18_CCP : IBytePeripheral, IKnownSize
    {
        public PIC18_CCP(IMachine machine, long baseFrequency = 4_000_000)
        {
            IRQ = new GPIO();
            PWMOutput = new GPIO();

            pwmTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                      this, "PIC18_CCP",
                                      limit: 255,
                                      enabled: false,
                                      eventEnabled: true);
            pwmTimer.LimitReached += OnPWMPeriod;

            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.CCPxCON: return ccpcon;
            case Registers.CCPR1L:  return ccprL;
            case Registers.CCPR1H:  return ccprH;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_CCP: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.CCPxCON:
                ccpcon = value;
                ApplyMode();
                break;
            case Registers.CCPR1L:
                ccprL = value;
                break;
            case Registers.CCPR1H:
                ccprH = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_CCP: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            ccpcon = 0;
            ccprL  = 0;
            ccprH  = 0;
            pwmTimer.Enabled = false;
            pwmTimer.Value = 0;
            IRQ.Unset();
            PWMOutput.Unset();
        }

        public long Size => 0x03;
        public GPIO IRQ { get; }
        public GPIO PWMOutput { get; }

        private void ApplyMode()
        {
            int mode = ccpcon & 0x0F;
            pwmTimer.Enabled = (mode >= 0x0C); /* 0x0C-0x0F = PWM modes */
        }

        private void OnPWMPeriod()
        {
            PWMOutput.Set(true);
            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            CCPxCON = 0x00,
            CCPR1L  = 0x01,
            CCPR1H  = 0x02,
        }

        private byte ccpcon;
        private byte ccprL;
        private byte ccprH;

        private readonly LimitTimer pwmTimer;
    }
}
