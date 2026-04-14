//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// TMS320C28x eCAP peripheral (Enhanced Capture module).
//
// Register map (32-bit capture values via 16-bit word accesses, byte offsets):
//   0x00  TSCTR    — Time-Stamp Counter (low 16)
//   0x02  TSCTRH   — Time-Stamp Counter (high 16)
//   0x04  CTRPHS   — Counter Phase Offset (low 16)
//   0x06  CTRPHSH  — Counter Phase Offset (high 16)
//   0x08  CAP1     — Capture 1 (low 16)
//   0x0A  CAP1H    — Capture 1 (high 16)
//   0x0C  CAP2     — Capture 2 (low 16)
//   0x0E  CAP2H    — Capture 2 (high 16)
//   0x10  CAP3     — Capture 3 (low 16)
//   0x12  CAP3H    — Capture 3 (high 16)
//   0x14  CAP4     — Capture 4 (low 16)
//   0x16  CAP4H    — Capture 4 (high 16)
//   0x28  ECCTL1   — Capture Control 1
//   0x2A  ECCTL2   — Capture Control 2 (CAP/APWM, TSCTRSTOP, SYNCI_EN, etc.)
//   0x2C  ECEINT   — Capture Interrupt Enable
//   0x2E  ECFLG    — Capture Interrupt Flag
//   0x30  ECCLR    — Capture Interrupt Clear
//   0x32  ECFRC    — Capture Interrupt Force
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class C2000_eCAP : IWordPeripheral, IKnownSize
    {
        public C2000_eCAP(IMachine machine, long baseFrequency = 100_000_000)
        {
            IRQ = new GPIO();
            caps = new uint[4];
            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "C2000_eCAP",
                                        limit: 0xFFFFFFFFUL,
                                        enabled: false,
                                        eventEnabled: true,
                                        direction: Direction.Ascending);
            innerTimer.LimitReached += OnWrap;
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            /* Capture registers: 32-bit via low/high word pairs */
            if(offset >= 0x08 && offset <= 0x17)
            {
                int idx = (int)(offset - 0x08) / 4;
                bool high = ((offset - 0x08) % 4) >= 2;
                return high ? (ushort)(caps[idx] >> 16) : (ushort)(caps[idx] & 0xFFFF);
            }

            switch((Registers)offset)
            {
            case Registers.TSCTR:   return (ushort)(innerTimer.Value & 0xFFFF);
            case Registers.TSCTRH:  return (ushort)((innerTimer.Value >> 16) & 0xFFFF);
            case Registers.CTRPHS:  return (ushort)(ctrphs & 0xFFFF);
            case Registers.CTRPHSH: return (ushort)((ctrphs >> 16) & 0xFFFF);
            case Registers.ECCTL1:  return ecctl1;
            case Registers.ECCTL2:  return ecctl2;
            case Registers.ECEINT:  return eceint;
            case Registers.ECFLG:   return ecflg;
            case Registers.ECCLR:   return 0;
            case Registers.ECFRC:   return 0;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_eCAP: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            /* Capture registers (software-writable in APWM mode) */
            if(offset >= 0x08 && offset <= 0x17)
            {
                int idx = (int)(offset - 0x08) / 4;
                bool high = ((offset - 0x08) % 4) >= 2;
                if(high)
                    caps[idx] = (uint)((caps[idx] & 0x0000FFFF) | ((uint)value << 16));
                else
                    caps[idx] = (uint)((caps[idx] & 0xFFFF0000) | value);
                return;
            }

            switch((Registers)offset)
            {
            case Registers.TSCTR:
                tsLatch = (uint)((tsLatch & 0xFFFF0000) | value);
                innerTimer.Value = tsLatch;
                break;
            case Registers.TSCTRH:
                tsLatch = (uint)((tsLatch & 0x0000FFFF) | ((uint)value << 16));
                innerTimer.Value = tsLatch;
                break;
            case Registers.CTRPHS:
                ctrphs = (uint)((ctrphs & 0xFFFF0000) | value);
                break;
            case Registers.CTRPHSH:
                ctrphs = (uint)((ctrphs & 0x0000FFFF) | ((uint)value << 16));
                break;
            case Registers.ECCTL1:
                ecctl1 = value;
                break;
            case Registers.ECCTL2:
                ecctl2 = value;
                innerTimer.Enabled = (value & ECCTL2_TSCTRSTOP) != 0;
                break;
            case Registers.ECEINT:
                eceint = (ushort)(value & 0xFF);
                break;
            case Registers.ECCLR:
                ecflg &= (ushort)~value;
                UpdateIRQ();
                break;
            case Registers.ECFRC:
                ecflg |= (ushort)(value & 0xFF);
                UpdateIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_eCAP: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        /// <summary>Inject a capture event (simulates an edge on the input pin).</summary>
        public void CaptureEvent()
        {
            caps[captureIndex] = (uint)(innerTimer.Value & 0xFFFFFFFF);
            captureIndex = (captureIndex + 1) & 0x03;
            ecflg |= (ushort)(1 << (captureIndex + 1)); /* CEVTn flag */
            UpdateIRQ();
        }

        public void Reset()
        {
            ecctl1 = 0;
            ecctl2 = 0;
            eceint = 0;
            ecflg  = 0;
            ctrphs = 0;
            tsLatch = 0;
            captureIndex = 0;
            for(int i = 0; i < 4; i++) caps[i] = 0;
            innerTimer.Value = 0;
            innerTimer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x40;
        public GPIO IRQ { get; }

        private void OnWrap()
        {
            ecflg |= ECFLG_CTROVF;
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            if((ecflg & eceint) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            TSCTR   = 0x00,
            TSCTRH  = 0x02,
            CTRPHS  = 0x04,
            CTRPHSH = 0x06,
            ECCTL1  = 0x28,
            ECCTL2  = 0x2A,
            ECEINT  = 0x2C,
            ECFLG   = 0x2E,
            ECCLR   = 0x30,
            ECFRC   = 0x32,
        }

        private const ushort ECCTL2_TSCTRSTOP = (1 << 4);
        private const ushort ECFLG_CTROVF     = (1 << 5);

        private ushort ecctl1, ecctl2, eceint, ecflg;
        private uint ctrphs, tsLatch;
        private int captureIndex;
        private readonly uint[] caps;
        private readonly LimitTimer innerTimer;
    }
}
