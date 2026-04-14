//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// PIC18 Enhanced PWM peripheral (ECCP module — full bridge / half bridge).
//
// Register map (byte-wide):
//   0x00  ECCPxCON — Enhanced CCP Control (EPxM1:0, DCxB1:0, CCPxM3:0)
//   0x01  ECCPxAS  — Auto-Shutdown Control
//   0x02  PWMxCON  — PWM Dead-Band Delay
//   0x03  CCPR1L   — Duty cycle Low
//   0x04  CCPR1H   — Duty cycle High
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class PIC18_PWM : IBytePeripheral, IKnownSize
    {
        public PIC18_PWM(IMachine machine, long baseFrequency = 4_000_000)
        {
            IRQ = new GPIO();
            PWMOutputA = new GPIO();
            PWMOutputB = new GPIO();

            pwmTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                      this, "PIC18_PWM",
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
            case Registers.ECCPxCON: return eccpcon;
            case Registers.ECCPxAS:  return eccpas;
            case Registers.PWMxCON:  return pwmcon;
            case Registers.CCPR1L:   return ccprL;
            case Registers.CCPR1H:   return ccprH;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_PWM: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.ECCPxCON:
                eccpcon = value;
                ApplyConfig();
                break;
            case Registers.ECCPxAS:
                eccpas = value;
                break;
            case Registers.PWMxCON:
                pwmcon = value;
                break;
            case Registers.CCPR1L:
                ccprL = value;
                break;
            case Registers.CCPR1H:
                ccprH = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "PIC18_PWM: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            eccpcon = 0;
            eccpas  = 0;
            pwmcon  = 0;
            ccprL   = 0;
            ccprH   = 0;
            pwmTimer.Enabled = false;
            pwmTimer.Value = 0;
            IRQ.Unset();
            PWMOutputA.Unset();
            PWMOutputB.Unset();
        }

        public long Size => 0x05;
        public GPIO IRQ { get; }
        public GPIO PWMOutputA { get; }
        public GPIO PWMOutputB { get; }

        private void ApplyConfig()
        {
            int mode = eccpcon & 0x0F;
            pwmTimer.Enabled = (mode >= 0x0C);
        }

        private void OnPWMPeriod()
        {
            int bridgeMode = (eccpcon >> 6) & 0x03;
            PWMOutputA.Set(true);
            if(bridgeMode > 0)
                PWMOutputB.Set(true);

            IRQ.Set();
            IRQ.Unset();
        }

        private enum Registers : long
        {
            ECCPxCON = 0x00,
            ECCPxAS  = 0x01,
            PWMxCON  = 0x02,
            CCPR1L   = 0x03,
            CCPR1H   = 0x04,
        }

        private byte eccpcon;
        private byte eccpas;
        private byte pwmcon;
        private byte ccprL;
        private byte ccprH;

        private readonly LimitTimer pwmTimer;
    }
}
