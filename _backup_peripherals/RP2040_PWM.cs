//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 PWM — 8-slice PWM controller (each slice has channels A and B).
//
// Per-slice register map (32-bit, byte offsets per slice, stride = 0x14):
//   0x00  CHn_CSR  — Control/status (EN, PH_CORRECT, DIVMODE, PH_ADV, PH_RET)
//   0x04  CHn_DIV  — Clock divider (INT:FRAC)
//   0x08  CHn_CTR  — Direct counter access
//   0x0C  CHn_CC   — Channel A/B compare (CC_B[15:0] | CC_A[15:0])
//   0x10  CHn_TOP  — Wrap value
//
// Global registers at offset 0xA0:
//   0xA0  EN       — Enable bits for all slices
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class RP2040_PWM : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_PWM(IMachine machine)
        {
            IRQ = new GPIO();
            slices = new PwmSlice[SliceCount];
            for(int i = 0; i < SliceCount; i++)
                slices[i] = new PwmSlice();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            if(offset >= 0xA0)
            {
                switch((GlobalRegisters)(offset - 0xA0))
                {
                case GlobalRegisters.EN:   return globalEn;
                case GlobalRegisters.INTR: return rawIntr;
                case GlobalRegisters.INTE: return inte;
                case GlobalRegisters.INTF: return intf;
                case GlobalRegisters.INTS: return (rawIntr | intf) & inte;
                default:
                    this.Log(LogLevel.Warning,
                             "RP2040_PWM: Read from unknown global offset 0x{0:X}", offset);
                    return 0;
                }
            }

            int slice = (int)(offset / SliceStride);
            if(slice >= SliceCount)
            {
                this.Log(LogLevel.Warning,
                         "RP2040_PWM: Read from invalid slice {0}", slice);
                return 0;
            }

            var s = slices[slice];
            switch((SliceRegisters)(offset % SliceStride))
            {
            case SliceRegisters.CHn_CSR: return s.csr;
            case SliceRegisters.CHn_DIV: return s.div;
            case SliceRegisters.CHn_CTR: return s.ctr;
            case SliceRegisters.CHn_CC:  return s.cc;
            case SliceRegisters.CHn_TOP: return s.top;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_PWM: Read from unknown slice offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(offset >= 0xA0)
            {
                switch((GlobalRegisters)(offset - 0xA0))
                {
                case GlobalRegisters.EN:
                    globalEn = value & ((1u << SliceCount) - 1);
                    break;
                case GlobalRegisters.INTR:
                    rawIntr &= ~value;
                    UpdateIRQ();
                    break;
                case GlobalRegisters.INTE:
                    inte = value & ((1u << SliceCount) - 1);
                    UpdateIRQ();
                    break;
                case GlobalRegisters.INTF:
                    intf = value & ((1u << SliceCount) - 1);
                    UpdateIRQ();
                    break;
                default:
                    this.Log(LogLevel.Warning,
                             "RP2040_PWM: Write 0x{0:X8} to unknown global offset 0x{1:X}", value, offset);
                    break;
                }
                return;
            }

            int slice = (int)(offset / SliceStride);
            if(slice >= SliceCount)
            {
                this.Log(LogLevel.Warning,
                         "RP2040_PWM: Write to invalid slice {0}", slice);
                return;
            }

            var s = slices[slice];
            switch((SliceRegisters)(offset % SliceStride))
            {
            case SliceRegisters.CHn_CSR:
                s.csr = value & 0xFF;
                break;
            case SliceRegisters.CHn_DIV:
                s.div = value;
                break;
            case SliceRegisters.CHn_CTR:
                s.ctr = value & 0xFFFF;
                break;
            case SliceRegisters.CHn_CC:
                s.cc = value;
                break;
            case SliceRegisters.CHn_TOP:
                s.top = value & 0xFFFF;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_PWM: Write 0x{0:X8} to unknown slice offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            globalEn = 0;
            rawIntr  = 0;
            inte     = 0;
            intf     = 0;
            for(int i = 0; i < SliceCount; i++)
            {
                slices[i].csr = 0;
                slices[i].div = 0x10; /* default: integer divider = 1 */
                slices[i].ctr = 0;
                slices[i].cc  = 0;
                slices[i].top = 0xFFFF;
            }
            IRQ.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private void UpdateIRQ()
        {
            if(((rawIntr | intf) & inte) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private class PwmSlice
        {
            public uint csr, div, ctr, cc, top;
        }

        private enum SliceRegisters : long
        {
            CHn_CSR = 0x00,
            CHn_DIV = 0x04,
            CHn_CTR = 0x08,
            CHn_CC  = 0x0C,
            CHn_TOP = 0x10,
        }

        private enum GlobalRegisters : long
        {
            EN   = 0x00,
            INTR = 0x04,
            INTE = 0x08,
            INTF = 0x0C,
            INTS = 0x10,
        }

        private const int SliceCount  = 8;
        private const int SliceStride = 0x14;

        private uint globalEn, rawIntr, inte, intf;
        private readonly PwmSlice[] slices;
    }
}
