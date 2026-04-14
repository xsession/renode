//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// MCS-51 Watchdog Timer (vendor-extended, common in AT89S52).
// Typical SFR: WDTRST at a single address — writing 0x1E then 0xE1 resets the WDT.
// Register: WDTRST (0x00).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class MCS51_WDT : IBytePeripheral, IKnownSize
    {
        public MCS51_WDT(IMachine machine, long frequency = 11_059_200)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, frequency / 12,
                                   this, "MCS51_WDT",
                                   limit: 16384,
                                   enabled: false,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Ascending);
            timer.LimitReached += OnTimeout;
            Reset();
        }

        public byte ReadByte(long offset)
        {
            if(offset == 0) return 0; // WDTRST is write-only
            this.Log(LogLevel.Warning, "MCS51_WDT: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            if(offset == 0)
            {
                if(value == 0x1E && keyState == 0)
                {
                    keyState = 1;
                }
                else if(value == 0xE1 && keyState == 1)
                {
                    // Successful key sequence — reset & enable watchdog
                    timer.Value = 0;
                    timer.Enabled = true;
                    keyState = 0;
                }
                else
                {
                    keyState = 0;
                }
            }
        }

        public void Reset()
        {
            keyState = 0;
            timer.Value = 0;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x01;
        public GPIO IRQ { get; }

        private void OnTimeout()
        {
            this.Log(LogLevel.Warning, "MCS51_WDT: Watchdog timeout");
            IRQ.Set();
            IRQ.Unset();
        }

        private int keyState;
        private readonly LimitTimer timer;
    }
}
