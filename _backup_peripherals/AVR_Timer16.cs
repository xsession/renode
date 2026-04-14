//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// AVR 16-bit Timer/Counter peripheral (Timer1 style).
//
// Register map (byte-wide I/O, 16-bit regs accessed as two bytes):
//   0x00  TCCR1A — Timer/Counter1 Control A (COM1x, WGM11:10)
//   0x01  TCCR1B — Timer/Counter1 Control B (ICNC, ICES, WGM13:12, CS12:10)
//   0x02  TCCR1C — Timer/Counter1 Control C (FOC1A, FOC1B)
//   0x04  TCNT1L — Timer/Counter1 Low byte
//   0x05  TCNT1H — Timer/Counter1 High byte
//   0x06  ICR1L  — Input Capture Register 1 Low
//   0x07  ICR1H  — Input Capture Register 1 High
//   0x08  OCR1AL — Output Compare Register 1A Low
//   0x09  OCR1AH — Output Compare Register 1A High
//   0x0A  OCR1BL — Output Compare Register 1B Low
//   0x0B  OCR1BH — Output Compare Register 1B High
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class AVR_Timer16 : IBytePeripheral, IKnownSize
    {
        public AVR_Timer16(IMachine machine, long baseFrequency = 16_000_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ_OVF   = new GPIO();
            IRQ_COMPA = new GPIO();
            IRQ_COMPB = new GPIO();
            IRQ_CAPT  = new GPIO();

            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "AVR_Timer16",
                                        limit: 0xFFFF,
                                        enabled: false,
                                        eventEnabled: true);
            innerTimer.LimitReached += OnOverflow;

            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TCCR1A: return tccr1a;
            case Registers.TCCR1B: return tccr1b;
            case Registers.TCCR1C: return tccr1c;
            case Registers.TCNT1L: return (byte)(innerTimer.Value & 0xFF);
            case Registers.TCNT1H: return (byte)((innerTimer.Value >> 8) & 0xFF);
            case Registers.ICR1L:  return (byte)(icr1 & 0xFF);
            case Registers.ICR1H:  return (byte)((icr1 >> 8) & 0xFF);
            case Registers.OCR1AL: return (byte)(ocr1a & 0xFF);
            case Registers.OCR1AH: return (byte)((ocr1a >> 8) & 0xFF);
            case Registers.OCR1BL: return (byte)(ocr1b & 0xFF);
            case Registers.OCR1BH: return (byte)((ocr1b >> 8) & 0xFF);
            default:
                this.Log(LogLevel.Warning,
                         "AVR_Timer16: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.TCCR1A:
                tccr1a = value;
                break;
            case Registers.TCCR1B:
                tccr1b = value;
                ApplyClockSelect();
                break;
            case Registers.TCCR1C:
                tccr1c = value;
                break;
            case Registers.TCNT1L:
                tempByte = value;
                break;
            case Registers.TCNT1H:
                innerTimer.Value = (uint)((value << 8) | tempByte);
                break;
            case Registers.ICR1L:
                icr1 = (ushort)((icr1 & 0xFF00) | value);
                break;
            case Registers.ICR1H:
                icr1 = (ushort)((icr1 & 0x00FF) | (value << 8));
                break;
            case Registers.OCR1AL:
                ocr1a = (ushort)((ocr1a & 0xFF00) | value);
                break;
            case Registers.OCR1AH:
                ocr1a = (ushort)((ocr1a & 0x00FF) | (value << 8));
                break;
            case Registers.OCR1BL:
                ocr1b = (ushort)((ocr1b & 0xFF00) | value);
                break;
            case Registers.OCR1BH:
                ocr1b = (ushort)((ocr1b & 0x00FF) | (value << 8));
                break;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_Timer16: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            tccr1a   = 0;
            tccr1b   = 0;
            tccr1c   = 0;
            icr1     = 0;
            ocr1a    = 0;
            ocr1b    = 0;
            tempByte = 0;
            innerTimer.Enabled = false;
            innerTimer.Value   = 0;
            IRQ_OVF.Unset();
            IRQ_COMPA.Unset();
            IRQ_COMPB.Unset();
            IRQ_CAPT.Unset();
        }

        public long Size => 0x0C;

        public GPIO IRQ_OVF   { get; }
        public GPIO IRQ_COMPA { get; }
        public GPIO IRQ_COMPB { get; }
        public GPIO IRQ_CAPT  { get; }

        private void OnOverflow()
        {
            IRQ_OVF.Set();
            IRQ_OVF.Unset();
        }

        private void ApplyClockSelect()
        {
            int cs = tccr1b & 0x07;
            switch(cs)
            {
            case 0: innerTimer.Enabled = false; break;
            case 1: innerTimer.Divider = 1;    innerTimer.Enabled = true; break;
            case 2: innerTimer.Divider = 8;    innerTimer.Enabled = true; break;
            case 3: innerTimer.Divider = 64;   innerTimer.Enabled = true; break;
            case 4: innerTimer.Divider = 256;  innerTimer.Enabled = true; break;
            case 5: innerTimer.Divider = 1024; innerTimer.Enabled = true; break;
            default:
                innerTimer.Enabled = false;
                break;
            }
        }

        private enum Registers : long
        {
            TCCR1A = 0x00,
            TCCR1B = 0x01,
            TCCR1C = 0x02,
            /* 0x03 reserved */
            TCNT1L = 0x04,
            TCNT1H = 0x05,
            ICR1L  = 0x06,
            ICR1H  = 0x07,
            OCR1AL = 0x08,
            OCR1AH = 0x09,
            OCR1BL = 0x0A,
            OCR1BH = 0x0B,
        }

        private byte   tccr1a;
        private byte   tccr1b;
        private byte   tccr1c;
        private ushort icr1;
        private ushort ocr1a;
        private ushort ocr1b;
        private byte   tempByte; /* AVR 16-bit temp register for atomic access */

        private readonly long baseFrequency;
        private readonly LimitTimer innerTimer;
    }
}
