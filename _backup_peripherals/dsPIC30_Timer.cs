//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC30 Timer peripheral (16-bit Type A / Type B timer).
//
// Register map (word-wide, dsPIC30F Family Reference Manual, Section 12):
//   0x00  TMRx   — Timer count register
//   0x02  PRx    — Period register
//   0x04  TxCON  — Timer control (TON, TSIDL, TGATE, TCKPS, T32, TSYNC, TCS)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class dsPIC30_Timer : IWordPeripheral, IKnownSize
    {
        public dsPIC30_Timer(IMachine machine, long baseFrequency = 30_000_000)
        {
            IRQ = new GPIO();
            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "dsPIC30_Timer",
                                        limit: 0xFFFF,
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
            case Registers.TMRx:  return (ushort)(innerTimer.Value & 0xFFFF);
            case Registers.PRx:   return (ushort)(innerTimer.Limit & 0xFFFF);
            case Registers.TxCON: return tcon;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_Timer: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.TMRx:
                innerTimer.Value = value;
                break;
            case Registers.PRx:
                innerTimer.Limit = value;
                break;
            case Registers.TxCON:
                tcon = value;
                innerTimer.Enabled = (value & TCON_TON) != 0;
                ApplyPrescaler(value);
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_Timer: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            tcon = 0;
            innerTimer.Value = 0;
            innerTimer.Limit = 0xFFFF;
            innerTimer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x06;
        public GPIO IRQ { get; }

        private void OnPeriodMatch()
        {
            IRQ.Set();
            IRQ.Unset();
        }

        private void ApplyPrescaler(ushort value)
        {
            int tckps = (value >> 4) & 0x03;
            int div = tckps switch
            {
                0 => 1,
                1 => 8,
                2 => 64,
                3 => 256,
                _ => 1,
            };
            innerTimer.Divider = div;
        }

        private enum Registers : long
        {
            TMRx  = 0x00,
            PRx   = 0x02,
            TxCON = 0x04,
        }

        private const ushort TCON_TON = (1 << 15);

        private ushort tcon;
        private readonly LimitTimer innerTimer;
    }
}
