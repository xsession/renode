//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 PIO — Programmable I/O block (basic stub model).
//
// The RP2040 has 2 PIO blocks, each with 4 state machines and 32-word instruction memory.
// This model provides register-level access for firmware compatibility but does not
// execute PIO programs (state machines are not cycle-accurate).
//
// Register map (32-bit, byte offsets):
//   0x000  CTRL         — PIO control (SM_ENABLE, SM_RESTART, CLKDIV_RESTART)
//   0x004  FSTAT        — FIFO status (TXFULL, TXEMPTY, RXFULL, RXEMPTY)
//   0x008  FDEBUG       — FIFO debug (TXSTALL, TXOVER, RXUNDER, RXSTALL)
//   0x00C  FLEVEL       — FIFO levels (TX0-TX3, RX0-RX3)
//   0x010  TXF0–TXF3   — TX FIFO (write, per SM, stride 4)
//   0x020  RXF0–RXF3   — RX FIFO (read, per SM, stride 4)
//   0x030  IRQ          — IRQ register
//   0x034  IRQ_FORCE    — Force IRQ
//
//   Per SM (stride 0x18, SM0 base = 0x0C8):
//     +0x00  CLKDIV      — Clock divider
//     +0x04  EXECCTRL    — Execution control
//     +0x08  SHIFTCTRL   — Shift control
//     +0x0C  ADDR        — Current instruction address
//     +0x10  INSTR       — Current instruction (read) / execute (write)
//     +0x14  PINCTRL     — Pin control
//
//   Instruction memory: 0x048–0x0C4 (32 words)
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class RP2040_PIO : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_PIO(IMachine machine)
        {
            IRQ = new GPIO();
            instrMem = new uint[InstrMemSize];
            smRegs = new SmRegisters[SmCount];
            txFifos = new Queue<uint>[SmCount];
            rxFifos = new Queue<uint>[SmCount];
            for(int i = 0; i < SmCount; i++)
            {
                smRegs[i] = new SmRegisters();
                txFifos[i] = new Queue<uint>();
                rxFifos[i] = new Queue<uint>();
            }
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            /* Instruction memory reads */
            if(offset >= InstrMemBase && offset < InstrMemBase + InstrMemSize * 4)
            {
                int idx = (int)(offset - InstrMemBase) / 4;
                return instrMem[idx];
            }

            /* SM register reads */
            if(offset >= SmBase && offset < SmBase + SmCount * SmStride)
            {
                int sm = (int)(offset - SmBase) / SmStride;
                int reg = (int)(offset - SmBase) % SmStride;
                return ReadSmReg(sm, reg);
            }

            /* RX FIFO reads */
            if(offset >= 0x020 && offset < 0x020 + SmCount * 4)
            {
                int sm = (int)(offset - 0x020) / 4;
                if(rxFifos[sm].Count > 0) return rxFifos[sm].Dequeue();
                return 0;
            }

            switch((Registers)offset)
            {
            case Registers.CTRL:    return ctrl;
            case Registers.FSTAT:   return BuildFStat();
            case Registers.FDEBUG:  return fdebug;
            case Registers.FLEVEL:  return BuildFLevel();
            case Registers.PIO_IRQ: return pioIrq;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_PIO: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            /* Instruction memory writes */
            if(offset >= InstrMemBase && offset < InstrMemBase + InstrMemSize * 4)
            {
                int idx = (int)(offset - InstrMemBase) / 4;
                instrMem[idx] = value & 0xFFFF; /* 16-bit PIO instructions */
                return;
            }

            /* SM register writes */
            if(offset >= SmBase && offset < SmBase + SmCount * SmStride)
            {
                int sm = (int)(offset - SmBase) / SmStride;
                int reg = (int)(offset - SmBase) % SmStride;
                WriteSmReg(sm, reg, value);
                return;
            }

            /* TX FIFO writes */
            if(offset >= 0x010 && offset < 0x010 + SmCount * 4)
            {
                int sm = (int)(offset - 0x010) / 4;
                if(txFifos[sm].Count < FifoDepth)
                    txFifos[sm].Enqueue(value);
                return;
            }

            switch((Registers)offset)
            {
            case Registers.CTRL:
                ctrl = value;
                break;
            case Registers.FDEBUG:
                fdebug &= ~value; /* W1C */
                break;
            case Registers.PIO_IRQ:
                pioIrq &= ~(value & 0xFF);
                break;
            case Registers.IRQ_FORCE:
                pioIrq |= value & 0xFF;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_PIO: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            ctrl    = 0;
            fdebug  = 0;
            pioIrq  = 0;
            for(int i = 0; i < InstrMemSize; i++) instrMem[i] = 0;
            for(int i = 0; i < SmCount; i++)
            {
                smRegs[i].clkdiv   = 0x00010000; /* divider = 1.0 */
                smRegs[i].execctrl = 0x0001F000;
                smRegs[i].shiftctrl = 0x000C0000;
                smRegs[i].addr     = 0;
                smRegs[i].instr    = 0;
                smRegs[i].pinctrl  = 0x14000000;
                txFifos[i].Clear();
                rxFifos[i].Clear();
            }
            IRQ.Unset();
        }

        /// <summary>Push a value into a state machine's RX FIFO (from external sources).</summary>
        public void PushRxFifo(int sm, uint value)
        {
            if(sm >= 0 && sm < SmCount && rxFifos[sm].Count < FifoDepth)
                rxFifos[sm].Enqueue(value);
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private uint ReadSmReg(int sm, int reg)
        {
            var s = smRegs[sm];
            return reg switch
            {
                0x00 => s.clkdiv,
                0x04 => s.execctrl,
                0x08 => s.shiftctrl,
                0x0C => s.addr,
                0x10 => s.instr,
                0x14 => s.pinctrl,
                _    => 0,
            };
        }

        private void WriteSmReg(int sm, int reg, uint value)
        {
            var s = smRegs[sm];
            switch(reg)
            {
            case 0x00: s.clkdiv   = value; break;
            case 0x04: s.execctrl = value; break;
            case 0x08: s.shiftctrl = value; break;
            case 0x10: s.instr    = value & 0xFFFF; break;
            case 0x14: s.pinctrl  = value; break;
            }
        }

        private uint BuildFStat()
        {
            uint fstat = 0;
            for(int i = 0; i < SmCount; i++)
            {
                if(txFifos[i].Count == 0)        fstat |= (1u << (24 + i)); /* TXEMPTY */
                if(txFifos[i].Count >= FifoDepth) fstat |= (1u << (16 + i)); /* TXFULL */
                if(rxFifos[i].Count == 0)         fstat |= (1u << (8 + i));  /* RXEMPTY */
                if(rxFifos[i].Count >= FifoDepth) fstat |= (1u << (0 + i));  /* RXFULL */
            }
            return fstat;
        }

        private uint BuildFLevel()
        {
            uint fl = 0;
            for(int i = 0; i < SmCount; i++)
            {
                fl |= (uint)((txFifos[i].Count & 0x0F) << (i * 8));
                fl |= (uint)((rxFifos[i].Count & 0x0F) << (i * 8 + 4));
            }
            return fl;
        }

        private class SmRegisters
        {
            public uint clkdiv, execctrl, shiftctrl, addr, instr, pinctrl;
        }

        private enum Registers : long
        {
            CTRL      = 0x000,
            FSTAT     = 0x004,
            FDEBUG    = 0x008,
            FLEVEL    = 0x00C,
            PIO_IRQ   = 0x030,
            IRQ_FORCE = 0x034,
        }

        private const int SmCount      = 4;
        private const int SmStride     = 0x18;
        private const int SmBase       = 0x0C8;
        private const int InstrMemBase = 0x048;
        private const int InstrMemSize = 32;
        private const int FifoDepth    = 4;

        private uint ctrl, fdebug, pioIrq;
        private readonly uint[] instrMem;
        private readonly SmRegisters[] smRegs;
        private readonly Queue<uint>[] txFifos;
        private readonly Queue<uint>[] rxFifos;
        private readonly IMachine machine;
    }
}
