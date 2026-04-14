//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// 8051 (MCS-51) Timer 0/1 peripheral — TMOD/TCON/TH/TL model.
//
// Register map (byte-wide):
//   0x00  TCON  — Timer Control (TF1, TR1, TF0, TR0, IE1, IT1, IE0, IT0)
//   0x01  TMOD  — Timer Mode (GATE1, C/T1, M1_1, M0_1, GATE0, C/T0, M1_0, M0_0)
//   0x02  TL0   — Timer 0 Low byte
//   0x03  TH0   — Timer 0 High byte
//   0x04  TL1   — Timer 1 Low byte
//   0x05  TH1   — Timer 1 High byte
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class MCS51_Timer : IBytePeripheral, IKnownSize
    {
        public MCS51_Timer(IMachine machine, long baseFrequency = 11_059_200)
        {
            this.baseFrequency = baseFrequency;
            IRQ_T0 = new GPIO();
            IRQ_T1 = new GPIO();

            timer0 = new LimitTimer(machine.ClockSource, baseFrequency / 12,
                                    this, "MCS51_Timer0",
                                    limit: 0xFFFF,
                                    enabled: false,
                                    eventEnabled: true);
            timer0.LimitReached += OnTimer0Overflow;

            timer1 = new LimitTimer(machine.ClockSource, baseFrequency / 12,
                                    this, "MCS51_Timer1",
                                    limit: 0xFFFF,
                                    enabled: false,
                                    eventEnabled: true);
            timer1.LimitReached += OnTimer1Overflow;

            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TCON: return tcon;
            case Registers.TMOD: return tmod;
            case Registers.TL0:  return (byte)(timer0.Value & 0xFF);
            case Registers.TH0:  return (byte)((timer0.Value >> 8) & 0xFF);
            case Registers.TL1:  return (byte)(timer1.Value & 0xFF);
            case Registers.TH1:  return (byte)((timer1.Value >> 8) & 0xFF);
            default:
                this.Log(LogLevel.Warning,
                         "MCS51_Timer: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch((Registers)offset)
            {
            case Registers.TCON:
                tcon = value;
                timer0.Enabled = (tcon & TCON_TR0) != 0;
                timer1.Enabled = (tcon & TCON_TR1) != 0;
                break;
            case Registers.TMOD:
                tmod = value;
                ApplyTimerModes();
                break;
            case Registers.TL0:
                timer0.Value = (timer0.Value & 0xFF00) | value;
                break;
            case Registers.TH0:
                timer0.Value = (timer0.Value & 0x00FF) | ((uint)value << 8);
                break;
            case Registers.TL1:
                timer1.Value = (timer1.Value & 0xFF00) | value;
                break;
            case Registers.TH1:
                timer1.Value = (timer1.Value & 0x00FF) | ((uint)value << 8);
                break;
            default:
                this.Log(LogLevel.Warning,
                         "MCS51_Timer: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            tcon = 0;
            tmod = 0;
            timer0.Enabled = false;
            timer0.Value = 0;
            timer1.Enabled = false;
            timer1.Value = 0;
            IRQ_T0.Unset();
            IRQ_T1.Unset();
        }

        public long Size => 0x06;

        public GPIO IRQ_T0 { get; }
        public GPIO IRQ_T1 { get; }

        private void OnTimer0Overflow()
        {
            tcon |= TCON_TF0;
            IRQ_T0.Set();
            IRQ_T0.Unset();
        }

        private void OnTimer1Overflow()
        {
            tcon |= TCON_TF1;
            IRQ_T1.Set();
            IRQ_T1.Unset();
        }

        private void ApplyTimerModes()
        {
            /* Mode 0 bits for Timer 0: TMOD[1:0], Timer 1: TMOD[5:4] */
            int mode0 = tmod & 0x03;
            int mode1 = (tmod >> 4) & 0x03;

            switch(mode0)
            {
            case 0: timer0.Limit = 0x1FFF; break; /* Mode 0: 13-bit */
            case 1: timer0.Limit = 0xFFFF; break; /* Mode 1: 16-bit */
            case 2: timer0.Limit = 0xFF;   break; /* Mode 2: 8-bit auto-reload */
            case 3: timer0.Limit = 0xFF;   break; /* Mode 3: split timers */
            }

            switch(mode1)
            {
            case 0: timer1.Limit = 0x1FFF; break;
            case 1: timer1.Limit = 0xFFFF; break;
            case 2: timer1.Limit = 0xFF;   break;
            case 3: timer1.Enabled = false; break; /* Timer 1 stopped in mode 3 */
            }
        }

        private enum Registers : long
        {
            TCON = 0x00,
            TMOD = 0x01,
            TL0  = 0x02,
            TH0  = 0x03,
            TL1  = 0x04,
            TH1  = 0x05,
        }

        private const byte TCON_TR0 = (1 << 4);
        private const byte TCON_TF0 = (1 << 5);
        private const byte TCON_TR1 = (1 << 6);
        private const byte TCON_TF1 = (byte)(1 << 7);

        private byte tcon;
        private byte tmod;

        private readonly long baseFrequency;
        private readonly LimitTimer timer0;
        private readonly LimitTimer timer1;
    }
}
