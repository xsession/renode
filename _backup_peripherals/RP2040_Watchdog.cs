//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 Watchdog — System watchdog timer.
//
// Register map (32-bit, byte offsets):
//   0x00  CTRL     — Control (TIME[23:0], PAUSE_JTAG, PAUSE_DBG0, PAUSE_DBG1, ENABLE, TRIGGER)
//   0x04  LOAD     — Load value (written to reload counter)
//   0x08  REASON   — Reset reason (TIMER, FORCE)
//   0x0C  SCRATCH0 — Scratch register 0 (survives watchdog reset)
//   0x10  SCRATCH1 — Scratch register 1
//   0x14  SCRATCH2 — Scratch register 2
//   0x18  SCRATCH3 — Scratch register 3
//   0x1C  SCRATCH4 — Scratch register 4
//   0x20  SCRATCH5 — Scratch register 5
//   0x24  SCRATCH6 — Scratch register 6
//   0x28  SCRATCH7 — Scratch register 7
//   0x2C  TICK     — Watchdog tick generator (ENABLE, COUNT, CYCLES)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class RP2040_Watchdog : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_Watchdog(IMachine machine)
        {
            IRQ = new GPIO();
            scratch = new uint[8];
            innerTimer = new LimitTimer(machine.ClockSource, 1_000_000,
                                        this, "RP2040_Watchdog",
                                        limit: 0xFFFFFF,
                                        enabled: false,
                                        eventEnabled: true,
                                        direction: Direction.Descending);
            innerTimer.LimitReached += OnTimeout;
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            if(offset >= 0x0C && offset <= 0x28)
            {
                int idx = (int)(offset - 0x0C) / 4;
                return scratch[idx];
            }

            switch((Registers)offset)
            {
            case Registers.CTRL:
                uint val = ctrl & ~CTRL_TIME_MASK;
                val |= (uint)(innerTimer.Value & 0xFFFFFF);
                return val;
            case Registers.LOAD:   return 0;
            case Registers.REASON: return reason;
            case Registers.TICK:   return tick;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_Watchdog: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(offset >= 0x0C && offset <= 0x28)
            {
                int idx = (int)(offset - 0x0C) / 4;
                scratch[idx] = value;
                return;
            }

            switch((Registers)offset)
            {
            case Registers.CTRL:
                ctrl = value;
                innerTimer.Enabled = (value & CTRL_ENABLE) != 0;
                if((value & CTRL_TRIGGER) != 0)
                {
                    innerTimer.Value = innerTimer.Limit;
                }
                break;
            case Registers.LOAD:
                innerTimer.Limit = value & 0xFFFFFF;
                innerTimer.Value = value & 0xFFFFFF;
                break;
            case Registers.REASON:
                reason = 0; /* write clears */
                break;
            case Registers.TICK:
                tick = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_Watchdog: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            ctrl   = 0;
            reason = 0;
            tick   = 0;
            innerTimer.Enabled = false;
            innerTimer.Limit = 0xFFFFFF;
            for(int i = 0; i < 8; i++) scratch[i] = 0;
            IRQ.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private void OnTimeout()
        {
            this.Log(LogLevel.Warning, "RP2040_Watchdog: Timeout! (reset would occur in real HW)");
            reason |= REASON_TIMER;
            IRQ.Set();
        }

        private enum Registers : long
        {
            CTRL   = 0x00,
            LOAD   = 0x04,
            REASON = 0x08,
            TICK   = 0x2C,
        }

        private const uint CTRL_TIME_MASK = 0x00FFFFFF;
        private const uint CTRL_ENABLE    = (1u << 30);
        private const uint CTRL_TRIGGER   = (1u << 31);
        private const uint REASON_TIMER   = (1u << 0);

        private uint ctrl, reason, tick;
        private readonly uint[] scratch;
        private readonly LimitTimer innerTimer;
    }
}
