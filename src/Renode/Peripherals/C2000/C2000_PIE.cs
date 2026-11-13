//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x PIE (Peripheral Interrupt Expansion) controller.
// 12 groups × 16 interrupts = 192 interrupt sources. 
// Provides vectored interrupt dispatch and priority management.
//
// Register map (from PIE base, typically 0x000CE0):
//   0x00  PIECTRL    — PIE control register
//   0x02  PIEACK     — PIE acknowledge register (w1c)
//   0x04–0x1A  PIEIER1–PIEIER12  — group enable registers (2 bytes each)
//   0x1C–0x32  PIEIFR1–PIEIFR12  — group flag registers (2 bytes each)
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class C2000_PIE : IWordPeripheral, IKnownSize
    {
        public C2000_PIE()
        {
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            if(offset == 0x00) return piectrl;
            if(offset == 0x02) return pieack;

            int idx;
            if(offset >= 0x04 && offset <= 0x1A)
            {
                idx = (int)(offset - 0x04) / 2;
                if(idx < NUM_GROUPS) return pieier[idx];
            }
            if(offset >= 0x1C && offset <= 0x32)
            {
                idx = (int)(offset - 0x1C) / 2;
                if(idx < NUM_GROUPS) return pieifr[idx];
            }

            /* PIE vector table: 0x40-0x1FF */
            if(offset >= VECTOR_TABLE_OFFSET && offset < VECTOR_TABLE_OFFSET + VECTOR_TABLE_SIZE)
            {
                int vi = (int)(offset - VECTOR_TABLE_OFFSET) / 2;
                if(vi < vectorTable.Length) return vectorTable[vi];
            }

            this.Log(LogLevel.Warning, "C2000_PIE: Read from unknown offset 0x{0:X}", offset);
            return 0;
        }

        public void WriteWord(long offset, ushort value)
        {
            if(offset == 0x00)
            {
                piectrl = (ushort)(value & 0x0001); /* only ENPIE bit */
                return;
            }
            if(offset == 0x02)
            {
                /* Write-1-to-clear acknowledge bits */
                pieack = (ushort)(pieack & ~value);
                return;
            }

            int idx;
            if(offset >= 0x04 && offset <= 0x1A)
            {
                idx = (int)(offset - 0x04) / 2;
                if(idx < NUM_GROUPS) pieier[idx] = value;
                return;
            }
            if(offset >= 0x1C && offset <= 0x32)
            {
                idx = (int)(offset - 0x1C) / 2;
                if(idx < NUM_GROUPS) pieifr[idx] = value;
                return;
            }

            /* PIE vector table writes */
            if(offset >= VECTOR_TABLE_OFFSET && offset < VECTOR_TABLE_OFFSET + VECTOR_TABLE_SIZE)
            {
                int vi = (int)(offset - VECTOR_TABLE_OFFSET) / 2;
                if(vi < vectorTable.Length) vectorTable[vi] = value;
                return;
            }

            this.Log(LogLevel.Warning, "C2000_PIE: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
        }

        public void SetInterrupt(int group, int channel)
        {
            if(group < 0 || group >= NUM_GROUPS || channel < 0 || channel >= 16) return;
            pieifr[group] |= (ushort)(1 << channel);
        }

        public void Reset()
        {
            piectrl = 0;
            pieack = 0;
            pieier = new ushort[NUM_GROUPS];
            pieifr = new ushort[NUM_GROUPS];
            vectorTable = new ushort[NUM_GROUPS * 16];
        }

        public long Size => 0x200;

        private const int NUM_GROUPS = 12;
        private const int VECTOR_TABLE_OFFSET = 0x40;
        private const int VECTOR_TABLE_SIZE = NUM_GROUPS * 16 * 2;

        private ushort piectrl;
        private ushort pieack;
        private ushort[] pieier;
        private ushort[] pieifr;
        private ushort[] vectorTable;
    }
}
