//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// TMS320C28x ePWM peripheral (Enhanced PWM module).
//
// Register map (16-bit word addresses, byte offsets — key registers):
//   0x00  TBCTL    — Time-Base Control (FREE/SOFT, CLKDIV, HSPCLKDIV, CTRMODE)
//   0x02  TBSTS    — Time-Base Status (CTRMAX, SYNCI, CTRDIR)
//   0x04  TBPHS    — Time-Base Phase
//   0x06  TBCTR    — Time-Base Counter
//   0x08  TBPRD    — Time-Base Period
//   0x0C  CMPA     — Counter-Compare A
//   0x0E  CMPB     — Counter-Compare B
//   0x10  AQCTLA   — Action-Qualifier Control (output A)
//   0x12  AQCTLB   — Action-Qualifier Control (output B)
//   0x18  DBCTL    — Dead-Band Control
//   0x1A  DBRED    — Dead-Band Rising Edge Delay
//   0x1C  DBFED    — Dead-Band Falling Edge Delay
//   0x20  ETSEL    — Event-Trigger Selection
//   0x22  ETPS     — Event-Trigger Pre-Scale
//   0x24  ETFLG    — Event-Trigger Flag
//   0x26  ETCLR    — Event-Trigger Clear
//   0x28  ETFRC    — Event-Trigger Force
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class C2000_ePWM : IWordPeripheral, IKnownSize
    {
        public C2000_ePWM(IMachine machine, long baseFrequency = 100_000_000)
        {
            IRQ = new GPIO();
            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "C2000_ePWM",
                                        limit: 0xFFFFUL,
                                        enabled: false,
                                        eventEnabled: true,
                                        direction: Direction.Ascending);
            innerTimer.LimitReached += OnPeriodMatch;
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TBCTL:  return tbctl;
            case Registers.TBSTS:  return tbsts;
            case Registers.TBPHS:  return tbphs;
            case Registers.TBCTR:  return (ushort)(innerTimer.Value & 0xFFFF);
            case Registers.TBPRD:  return (ushort)(innerTimer.Limit & 0xFFFF);
            case Registers.CMPA:   return cmpa;
            case Registers.CMPB:   return cmpb;
            case Registers.AQCTLA: return aqctla;
            case Registers.AQCTLB: return aqctlb;
            case Registers.DBCTL:  return dbctl;
            case Registers.DBRED:  return dbred;
            case Registers.DBFED:  return dbfed;
            case Registers.ETSEL:  return etsel;
            case Registers.ETPS:   return etps;
            case Registers.ETFLG:  return etflg;
            case Registers.ETCLR:  return 0;
            case Registers.ETFRC:  return 0;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_ePWM: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.TBCTL:
                tbctl = value;
                int ctrMode = value & 0x03;
                innerTimer.Enabled = ctrMode != CTRMODE_FREEZE;
                if(ctrMode == CTRMODE_UPDOWN)
                    innerTimer.Direction = Direction.Ascending; /* simplified */
                break;
            case Registers.TBSTS:
                tbsts &= (ushort)~(value & 0x07); /* W1C for lower bits */
                break;
            case Registers.TBPHS:
                tbphs = value;
                break;
            case Registers.TBCTR:
                innerTimer.Value = value;
                break;
            case Registers.TBPRD:
                innerTimer.Limit = value;
                break;
            case Registers.CMPA:
                cmpa = value;
                break;
            case Registers.CMPB:
                cmpb = value;
                break;
            case Registers.AQCTLA:
                aqctla = value;
                break;
            case Registers.AQCTLB:
                aqctlb = value;
                break;
            case Registers.DBCTL:
                dbctl = value;
                break;
            case Registers.DBRED:
                dbred = value;
                break;
            case Registers.DBFED:
                dbfed = value;
                break;
            case Registers.ETSEL:
                etsel = value;
                break;
            case Registers.ETPS:
                etps = value;
                break;
            case Registers.ETCLR:
                etflg &= (ushort)~(value & 0x0F);
                UpdateIRQ();
                break;
            case Registers.ETFRC:
                etflg |= (ushort)(value & 0x0F);
                UpdateIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_ePWM: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            tbctl  = 0;
            tbsts  = 0;
            tbphs  = 0;
            cmpa   = 0;
            cmpb   = 0;
            aqctla = 0;
            aqctlb = 0;
            dbctl  = 0;
            dbred  = 0;
            dbfed  = 0;
            etsel  = 0;
            etps   = 0;
            etflg  = 0;
            innerTimer.Value = 0;
            innerTimer.Limit = 0xFFFF;
            innerTimer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x40;
        public GPIO IRQ { get; }

        private void OnPeriodMatch()
        {
            tbsts |= 0x01; /* CTRMAX */
            etflg |= 0x01; /* ET interrupt */
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            bool intEn = (etsel & ETSEL_INTEN) != 0;
            if(intEn && (etflg & 0x01) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            TBCTL  = 0x00,
            TBSTS  = 0x02,
            TBPHS  = 0x04,
            TBCTR  = 0x06,
            TBPRD  = 0x08,
            CMPA   = 0x0C,
            CMPB   = 0x0E,
            AQCTLA = 0x10,
            AQCTLB = 0x12,
            DBCTL  = 0x18,
            DBRED  = 0x1A,
            DBFED  = 0x1C,
            ETSEL  = 0x20,
            ETPS   = 0x22,
            ETFLG  = 0x24,
            ETCLR  = 0x26,
            ETFRC  = 0x28,
        }

        private const int CTRMODE_FREEZE = 0x03;
        private const int CTRMODE_UPDOWN = 0x02;
        private const ushort ETSEL_INTEN = (1 << 3);

        private ushort tbctl, tbsts, tbphs;
        private ushort cmpa, cmpb;
        private ushort aqctla, aqctlb;
        private ushort dbctl, dbred, dbfed;
        private ushort etsel, etps, etflg;
        private readonly LimitTimer innerTimer;
    }
}
