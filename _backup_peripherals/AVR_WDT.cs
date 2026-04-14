//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// AVR Watchdog Timer peripheral.
//
// Register map (byte-wide I/O):
//   0x00  WDTCSR — Watchdog Timer Control & Status Register
//
// Bits: WDIF(7), WDIE(6), WDP3(5), WDCE(4), WDE(3), WDP2:0(2:0)
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class AVR_WDT : IBytePeripheral, IKnownSize
    {
        public AVR_WDT(IMachine machine, long baseFrequency = 128_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ = new GPIO();

            wdTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                     this, "AVR_WDT",
                                     limit: 2048,
                                     enabled: false,
                                     eventEnabled: true);
            wdTimer.LimitReached += OnWatchdogTimeout;

            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
            case 0x00: return wdtcsr;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_WDT: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
            case 0x00:
                HandleWDTCSRWrite(value);
                break;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_WDT: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            wdtcsr = 0;
            changeEnabled = false;
            wdTimer.Enabled = false;
            wdTimer.Value = 0;
            IRQ.Unset();
        }

        /// <summary>Feed (reset) the watchdog timer.</summary>
        public void Feed()
        {
            wdTimer.Value = 0;
        }

        public long Size => 0x01;

        public GPIO IRQ { get; }

        private void HandleWDTCSRWrite(byte value)
        {
            /* Timed sequence: WDCE+WDE must be set first */
            if((value & (WDCE | WDE)) == (WDCE | WDE))
            {
                changeEnabled = true;
                wdtcsr = value;
                return;
            }

            if(changeEnabled)
            {
                wdtcsr = value;
                changeEnabled = false;
                ApplyPrescaler();
                wdTimer.Enabled = (wdtcsr & WDE) != 0 || (wdtcsr & WDIE) != 0;
            }
            else
            {
                /* Only WDIF can be cleared by writing 1 */
                if((value & WDIF) != 0)
                {
                    wdtcsr &= unchecked((byte)~WDIF);
                }
            }
        }

        private void ApplyPrescaler()
        {
            int wdp = (wdtcsr & 0x07) | (((wdtcsr >> 5) & 1) << 3);
            /* WDP values: 0=2K, 1=4K, 2=8K, 3=16K, 4=32K, 5=64K, 6=128K, 7=256K, 8=512K, 9=1024K */
            uint cycles = (uint)(2048 << wdp);
            if(cycles < 2048) cycles = 2048;
            wdTimer.Limit = cycles;
        }

        private void OnWatchdogTimeout()
        {
            wdtcsr |= WDIF;

            if((wdtcsr & WDIE) != 0)
            {
                /* Interrupt mode */
                IRQ.Set();
                IRQ.Unset();
                /* After interrupt, WDIE is cleared → next timeout is reset */
                wdtcsr &= unchecked((byte)~WDIE);
            }
            else if((wdtcsr & WDE) != 0)
            {
                /* System reset mode */
                this.Log(LogLevel.Warning, "AVR_WDT: Watchdog reset triggered");
            }
        }

        private const byte WDIF = (byte)(1 << 7);
        private const byte WDIE = (1 << 6);
        private const byte WDCE = (1 << 4);
        private const byte WDE  = (1 << 3);

        private byte wdtcsr;
        private bool changeEnabled;

        private readonly long baseFrequency;
        private readonly LimitTimer wdTimer;
    }
}
