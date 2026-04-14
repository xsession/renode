//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 Timer — 64-bit system timer with 4 alarm comparators.
//
// Register map (32-bit, byte offsets):
//   0x00  TIMEHW   — Write to bits 63:32 (armed on next write to TIMELW)
//   0x04  TIMELW   — Write to bits 31:0 and arm (latches TIMEHW)
//   0x08  TIMEHR   — Read bits 63:32 (latched on read of TIMELR)
//   0x0C  TIMELR   — Read bits 31:0 (latches TIMEHR)
//   0x10  ALARM0   — Alarm 0 target
//   0x14  ALARM1   — Alarm 1 target
//   0x18  ALARM2   — Alarm 2 target
//   0x1C  ALARM3   — Alarm 3 target
//   0x20  ARMED    — Arm status (write 1 to arm)
//   0x24  TIMERAWH — Raw 63:32 (not latched)
//   0x28  TIMERAWL — Raw 31:0 (not latched)
//   0x34  INTR     — Raw interrupt status
//   0x38  INTE     — Interrupt enable
//   0x3C  INTF     — Interrupt force
//   0x40  INTS     — Interrupt status (masked)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class RP2040_Timer : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_Timer(IMachine machine)
        {
            IRQ = new GPIO();
            alarms = new uint[AlarmCount];

            innerTimer = new LimitTimer(machine.ClockSource, 1_000_000, /* 1 MHz */
                                        this, "RP2040_Timer",
                                        limit: ulong.MaxValue,
                                        enabled: true,
                                        eventEnabled: false,
                                        direction: Direction.Ascending);
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TIMEHR:
                return latchedHigh;
            case Registers.TIMELR:
                ulong val = innerTimer.Value;
                latchedHigh = (uint)(val >> 32);
                return (uint)(val & 0xFFFFFFFF);
            case Registers.TIMERAWH:
                return (uint)(innerTimer.Value >> 32);
            case Registers.TIMERAWL:
                return (uint)(innerTimer.Value & 0xFFFFFFFF);
            case Registers.ALARM0: return alarms[0];
            case Registers.ALARM1: return alarms[1];
            case Registers.ALARM2: return alarms[2];
            case Registers.ALARM3: return alarms[3];
            case Registers.ARMED:  return armed;
            case Registers.INTR:   return rawIntr;
            case Registers.INTE:   return inte;
            case Registers.INTF:   return intf;
            case Registers.INTS:   return (rawIntr | intf) & inte;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_Timer: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.TIMEHW:
                writeHigh = value;
                break;
            case Registers.TIMELW:
                ulong combined = ((ulong)writeHigh << 32) | value;
                innerTimer.Value = combined;
                break;
            case Registers.ALARM0: alarms[0] = value; armed |= (1u << 0); break;
            case Registers.ALARM1: alarms[1] = value; armed |= (1u << 1); break;
            case Registers.ALARM2: alarms[2] = value; armed |= (1u << 2); break;
            case Registers.ALARM3: alarms[3] = value; armed |= (1u << 3); break;
            case Registers.ARMED:
                armed &= ~value; /* write 1 to disarm */
                break;
            case Registers.INTR:
                rawIntr &= ~value;
                UpdateIRQ();
                break;
            case Registers.INTE:
                inte = value & 0x0F;
                UpdateIRQ();
                break;
            case Registers.INTF:
                intf = value & 0x0F;
                UpdateIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_Timer: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            innerTimer.Value = 0;
            writeHigh   = 0;
            latchedHigh = 0;
            armed       = 0;
            rawIntr     = 0;
            inte        = 0;
            intf        = 0;
            for(int i = 0; i < AlarmCount; i++)
                alarms[i] = 0;
            IRQ.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private void UpdateIRQ()
        {
            if(((rawIntr | intf) & inte) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            TIMEHW   = 0x00,
            TIMELW   = 0x04,
            TIMEHR   = 0x08,
            TIMELR   = 0x0C,
            ALARM0   = 0x10,
            ALARM1   = 0x14,
            ALARM2   = 0x18,
            ALARM3   = 0x1C,
            ARMED    = 0x20,
            TIMERAWH = 0x24,
            TIMERAWL = 0x28,
            INTR     = 0x34,
            INTE     = 0x38,
            INTF     = 0x3C,
            INTS     = 0x40,
        }

        private const int AlarmCount = 4;

        private uint writeHigh, latchedHigh, armed;
        private uint rawIntr, inte, intf;
        private readonly uint[] alarms;
        private readonly LimitTimer innerTimer;
    }
}
