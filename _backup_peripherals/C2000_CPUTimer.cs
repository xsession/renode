//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x CPU Timer peripheral.
//
// The C28x has three identical CPU Timers (Timer 0, 1, 2):
//   - CPU Timer 0: general purpose (INT1)
//   - CPU Timer 1: reserved for OS (INT13)
//   - CPU Timer 2: reserved for BIOS/watchdog (INT14)
//
// Register map (32-bit word offsets, byte addresses):
//   0x00  TIM    — timer counter (low 16 bits, 32-bit register)
//   0x02  TIMH   — timer counter (high 16 bits)
//   0x04  PRD    — period (low 16 bits, 32-bit register)
//   0x06  PRDH   — period (high 16 bits)
//   0x08  TCR    — timer control register (16-bit, upper 16 unused)
//   0x0C  TPR    — timer prescale (lower 8 = TDDR, upper 8 = PSC)
//   0x0E  TPRH   — timer prescale high (TDDRH:PSCH)
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class C2000_CPUTimer : IWordPeripheral, IKnownSize
    {
        public C2000_CPUTimer(IMachine machine, long baseFrequency = 200_000_000)
        {
            this.baseFrequency = baseFrequency;
            IRQ = new GPIO();

            innerTimer = new LimitTimer(machine.ClockSource, baseFrequency,
                                        this, "C2000_CPUTimer",
                                        limit: 0xFFFFFFFFUL,
                                        enabled: false,
                                        eventEnabled: true,
                                        direction: Antmicro.Renode.Time.Direction.Descending);
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
            case Registers.TIM:   return (ushort)(innerTimer.Value & 0xFFFF);
            case Registers.TIMH:  return (ushort)((innerTimer.Value >> 16) & 0xFFFF);
            case Registers.PRD:   return (ushort)(innerTimer.Limit & 0xFFFF);
            case Registers.PRDH:  return (ushort)((innerTimer.Limit >> 16) & 0xFFFF);
            case Registers.TCR:   return tcr;
            case Registers.TPR:   return tpr;
            case Registers.TPRH:  return tprh;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_CPUTimer: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.TIM:
                /* Load counter as 32-bit merge */
                timLatch = (uint)((timLatch & 0xFFFF0000u) | value);
                innerTimer.Value = timLatch;
                break;
            case Registers.TIMH:
                timLatch = (uint)((timLatch & 0x0000FFFFu) | ((uint)value << 16));
                innerTimer.Value = timLatch;
                break;
            case Registers.PRD:
                prdLatch = (uint)((prdLatch & 0xFFFF0000u) | value);
                innerTimer.Limit = prdLatch;
                break;
            case Registers.PRDH:
                prdLatch = (uint)((prdLatch & 0x0000FFFFu) | ((uint)value << 16));
                innerTimer.Limit = prdLatch;
                break;
            case Registers.TCR:
                tcr = (ushort)(value & TCR_WRITABLE_MASK);

                /* TRB (bit 5): reload timer from PRD when set */
                if((value & TCR_TRB) != 0)
                {
                    innerTimer.Value = innerTimer.Limit;
                }

                /* TSS (bit 4): 1 = stop, 0 = start */
                bool run = (tcr & TCR_TSS) == 0;
                innerTimer.Enabled = run;

                /* TIF (bit 15): write 1 to clear */
                if((value & TCR_TIF) != 0)
                {
                    tcr &= unchecked((ushort)~TCR_TIF);
                    IRQ.Unset();
                }
                break;
            case Registers.TPR:
                tpr = value;
                ApplyPrescaler();
                break;
            case Registers.TPRH:
                tprh = value;
                ApplyPrescaler();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_CPUTimer: Write 0x{0:X} to unknown offset 0x{1:X}",
                         value, offset);
                break;
            }
        }

        public void Reset()
        {
            tcr      = TCR_TSS; /* stopped */
            tpr      = 0;
            tprh     = 0;
            timLatch = 0;
            prdLatch = 0xFFFFFFFFu;
            innerTimer.Limit   = 0xFFFFFFFFUL;
            innerTimer.Value   = 0xFFFFFFFFUL;
            innerTimer.Enabled = false;
            IRQ.Unset();
        }

        // ------------------------------------------------------------------
        // IKnownSize
        // ------------------------------------------------------------------

        public long Size => 0x10;

        // ------------------------------------------------------------------
        // GPIO (interrupt output)
        // ------------------------------------------------------------------

        public GPIO IRQ { get; }

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------

        private void OnLimitReached()
        {
            tcr |= TCR_TIF;
            if((tcr & TCR_TIE) != 0)
            {
                IRQ.Set();
                IRQ.Unset();
            }
        }

        private void ApplyPrescaler()
        {
            /* TDDR (bits 7:0 of TPR) is the prescale divide (0 = /1, n = /(n+1)) */
            int tddr = (tpr & 0xFF) + 1;
            innerTimer.Divider = tddr;
        }

        private enum Registers : long
        {
            TIM  = 0x00,
            TIMH = 0x02,
            PRD  = 0x04,
            PRDH = 0x06,
            TCR  = 0x08,
            TPR  = 0x0C,
            TPRH = 0x0E,
        }

        /* TCR bit definitions */
        private const ushort TCR_TIF           = (1 << 15);
        private const ushort TCR_TIE           = (1 << 14);
        private const ushort TCR_TSS           = (1 << 4);
        private const ushort TCR_TRB           = (1 << 5);
        private const ushort TCR_WRITABLE_MASK = 0x7FFF; /* TIF cleared only by w1c */

        private ushort tcr;
        private ushort tpr;
        private ushort tprh;
        private uint   timLatch;
        private uint   prdLatch;

        private readonly long baseFrequency;
        private readonly LimitTimer innerTimer;
    }
}
