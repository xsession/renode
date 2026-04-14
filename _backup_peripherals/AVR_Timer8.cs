//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// AVR 8-bit Timer/Counter (Timer0 / Timer2 style — 8-bit with prescaler).
//
// Register map (byte-wide I/O):
//   0x00  TCCRnA — timer/counter control A (WGM bits, compare output mode)
//   0x01  TCCRnB — timer/counter control B (WGM bit, clock select CS2:0)
//   0x02  TCNTn  — timer/counter value (read/write)
//   0x03  OCRnA  — output compare register A
//   0x04  OCRnB  — output compare register B
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class AVR_Timer8 : IBytePeripheral, IKnownSize
    {
        public AVR_Timer8(IMachine machine, long baseFrequency = 16_000_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ_OVF = new GPIO();
            IRQ_COMPA = new GPIO();

            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "AVR_Timer8",
                                        limit: 0xFF,
                                        enabled: false,
                                        eventEnabled: true);
            innerTimer.LimitReached += OnOverflow;

            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
            case 0x00: return tccrnA;
            case 0x01: return tccrnB;
            case 0x02: return (byte)(innerTimer.Value & 0xFF);
            case 0x03: return ocrnA;
            case 0x04: return ocrnB;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_Timer8: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
            case 0x00:
                tccrnA = value;
                break;
            case 0x01:
                tccrnB = value;
                ApplyClockSelect();
                break;
            case 0x02:
                innerTimer.Value = value;
                break;
            case 0x03:
                ocrnA = value;
                break;
            case 0x04:
                ocrnB = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_Timer8: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            tccrnA = 0;
            tccrnB = 0;
            ocrnA  = 0;
            ocrnB  = 0;
            innerTimer.Enabled = false;
            innerTimer.Value   = 0;
            IRQ_OVF.Unset();
            IRQ_COMPA.Unset();
        }

        public long Size => 0x05;

        public GPIO IRQ_OVF   { get; }
        public GPIO IRQ_COMPA { get; }

        private void OnOverflow()
        {
            IRQ_OVF.Set();
            IRQ_OVF.Unset();
        }

        private void ApplyClockSelect()
        {
            int cs = tccrnB & 0x07; /* CS2:CS1:CS0 */
            switch(cs)
            {
            case 0:
                innerTimer.Enabled = false;
                break;
            case 1:
                innerTimer.Divider = 1;
                innerTimer.Enabled = true;
                break;
            case 2:
                innerTimer.Divider = 8;
                innerTimer.Enabled = true;
                break;
            case 3:
                innerTimer.Divider = 64;
                innerTimer.Enabled = true;
                break;
            case 4:
                innerTimer.Divider = 256;
                innerTimer.Enabled = true;
                break;
            case 5:
                innerTimer.Divider = 1024;
                innerTimer.Enabled = true;
                break;
            default:
                /* External clock sources — not modelled */
                innerTimer.Enabled = false;
                break;
            }
        }

        private byte tccrnA;
        private byte tccrnB;
        private byte ocrnA;
        private byte ocrnB;

        private readonly long baseFrequency;
        private readonly LimitTimer innerTimer;
    }
}
