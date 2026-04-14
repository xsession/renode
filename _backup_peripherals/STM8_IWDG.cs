//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// STM8 Independent Watchdog (IWDG).
// Key register (KR): 0xCC=start, 0xAA=reload, 0x55=unlock PR/RLR.
// Registers: KR(0x00), PR(0x01), RLR(0x02).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class STM8_IWDG : IBytePeripheral, IKnownSize
    {
        public STM8_IWDG(IMachine machine, long lsiFrequency = 128_000)
        {
            IRQ = new GPIO();
            timer = new LimitTimer(machine.ClockSource, lsiFrequency,
                                   this, "STM8_IWDG",
                                   limit: 0xFF,
                                   enabled: false,
                                   eventEnabled: true,
                                   direction: Antmicro.Renode.Time.Direction.Descending);
            timer.LimitReached += OnTimeout;
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
                case 0x00: return 0; // KR is write-only
                case 0x01: return pr;
                case 0x02: return rlr;
            }
            this.Log(LogLevel.Warning, "STM8_IWDG: Read 0x{0:X}", offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
                case 0x00: // KR
                    if(value == KEY_START)
                    {
                        timer.Value = rlr;
                        timer.Enabled = true;
                        started = true;
                    }
                    else if(value == KEY_RELOAD)
                    {
                        timer.Value = rlr;
                    }
                    else if(value == KEY_UNLOCK)
                    {
                        unlocked = true;
                    }
                    return;
                case 0x01: // PR
                    if(unlocked)
                    {
                        pr = (byte)(value & 0x07);
                        int div = 4 << pr; // divider: 4,8,16,...,256
                        timer.Divider = div;
                    }
                    return;
                case 0x02: // RLR
                    if(unlocked)
                    {
                        rlr = value;
                        timer.Limit = rlr;
                    }
                    return;
            }
        }

        public void Reset()
        {
            pr = 0;
            rlr = 0xFF;
            unlocked = false;
            started = false;
            timer.Value = 0xFF;
            timer.Limit = 0xFF;
            timer.Enabled = false;
            IRQ.Unset();
        }

        public long Size => 0x03;
        public GPIO IRQ { get; }

        private void OnTimeout()
        {
            this.Log(LogLevel.Warning, "STM8_IWDG: Watchdog timeout — system reset");
            IRQ.Set();
            IRQ.Unset();
        }

        private const byte KEY_START  = 0xCC;
        private const byte KEY_RELOAD = 0xAA;
        private const byte KEY_UNLOCK = 0x55;

        private byte pr;
        private byte rlr;
        private bool unlocked;
        private bool started;
        private readonly LimitTimer timer;
    }
}
