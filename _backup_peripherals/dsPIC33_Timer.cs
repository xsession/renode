//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// dsPIC33 Timer peripheral (TIMERx module — Type A / Type B / Type C).
//
// Register map (word-wide):
//   0x00  TxCON  — timer control
//   0x02  TMRx   — timer counter (16-bit)
//   0x04  PRx    — period register
//
// The timer counts up from 0 to PRx, then resets and sets the TxIF flag.
// The clock source can be Tcy (instruction cycle) or external T1CK pin.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class dsPIC33_Timer : IWordPeripheral, IKnownSize
    {
        public dsPIC33_Timer(IMachine machine, long baseFrequency = 40_000_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ = new GPIO();

            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "dsPIC33_Timer",
                                        limit: 0xFFFF,
                                        enabled: false,
                                        eventEnabled: true);
            innerTimer.LimitReached += OnLimitReached;

            Reset();
        }

        // ------------------------------------------------------------------
        // IWordPeripheral
        // ------------------------------------------------------------------

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TxCON: return txcon;
            case Registers.TMRx:  return (ushort)innerTimer.Value;
            case Registers.PRx:   return (ushort)innerTimer.Limit;
            default:
                this.Log(LogLevel.Warning, "Timer read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.TxCON:
                txcon = value;
                ApplyControl();
                break;
            case Registers.TMRx:
                innerTimer.Value = value;
                break;
            case Registers.PRx:
                innerTimer.Limit = value == 0 ? 0xFFFF : value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "Timer write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        // ------------------------------------------------------------------
        // Reset
        // ------------------------------------------------------------------

        public void Reset()
        {
            txcon = 0;
            innerTimer.Reset();
            innerTimer.Limit   = 0xFFFF;
            innerTimer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x06;
        public GPIO IRQ  { get; }

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------

        private void ApplyControl()
        {
            // TON (bit 15): timer on/off
            bool ton = (txcon & TXCON_TON) != 0;

            // TCKPS (bits 5:4): prescaler  00=1:1 01=1:8 10=1:64 11=1:256
            int prescalerCode = (txcon >> TXCON_TCKPS_SHIFT) & 0x3;
            long[] prescalers = { 1, 8, 64, 256 };
            long prescaler = prescalers[prescalerCode];

            innerTimer.Divider = prescaler;
            innerTimer.Enabled = ton;
        }

        private void OnLimitReached()
        {
            IRQ.Set();
            innerTimer.Value = 0;
        }

        private enum Registers : long
        {
            TxCON = 0x00,
            TMRx  = 0x02,
            PRx   = 0x04,
        }

        private const ushort TXCON_TON         = 1 << 15;
        private const int    TXCON_TCKPS_SHIFT = 4;

        private ushort txcon;
        private readonly long baseFrequency;
        private readonly LimitTimer innerTimer;
    }
}
