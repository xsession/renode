//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 SSI — Synchronous Serial Interface (QSPI controller for XIP flash).
//
// This is a DW_apb_ssi (Synopsys DesignWare SSI) compatible controller.
// Used to access the external QSPI flash in XIP (execute-in-place) mode.
//
// Register map (32-bit, byte offsets):
//   0x00  CTRLR0   — Control register 0 (DFS, FRF, SCPH, SCPOL, TMOD, etc.)
//   0x04  CTRLR1   — Control register 1 (NDF: Number of Data Frames)
//   0x08  SSIENR   — SSI Enable
//   0x10  SER      — Slave Enable Register (chip select)
//   0x14  BAUDR    — Baud rate select
//   0x18  TXFTLR   — TX FIFO threshold
//   0x1C  RXFTLR   — RX FIFO threshold
//   0x20  TXFLR    — TX FIFO level
//   0x24  RXFLR    — RX FIFO level
//   0x28  SR       — Status register (BUSY, TFNF, TFE, RFNE, RFF, etc.)
//   0x2C  IMR      — Interrupt mask
//   0x30  ISR      — Interrupt status
//   0x34  RISR     — Raw interrupt status
//   0x48  ICR      — Interrupt clear
//   0x60  DR0      — Data register (TX/RX)
//   0xF0  SPI_CTRLR0 — SPI-specific control (address/instruction length, etc.)
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class RP2040_SSI : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_SSI(IMachine machine)
        {
            IRQ = new GPIO();
            txFifo = new Queue<uint>();
            rxFifo = new Queue<uint>();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.CTRLR0:     return ctrlr0;
            case Registers.CTRLR1:     return ctrlr1;
            case Registers.SSIENR:     return ssienr;
            case Registers.SER:        return ser;
            case Registers.BAUDR:      return baudr;
            case Registers.TXFTLR:     return txftlr;
            case Registers.RXFTLR:     return rxftlr;
            case Registers.TXFLR:      return (uint)txFifo.Count;
            case Registers.RXFLR:      return (uint)rxFifo.Count;
            case Registers.SR:         return BuildSR();
            case Registers.IMR:        return imr;
            case Registers.ISR:        return risr & imr;
            case Registers.RISR:       return risr;
            case Registers.ICR:
                risr = 0;
                IRQ.Unset();
                return 0;
            case Registers.DR0:
                if(rxFifo.Count > 0) return rxFifo.Dequeue();
                return 0;
            case Registers.SPI_CTRLR0: return spiCtrlr0;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_SSI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.CTRLR0:
                ctrlr0 = value;
                break;
            case Registers.CTRLR1:
                ctrlr1 = value & 0xFFFF;
                break;
            case Registers.SSIENR:
                ssienr = value & 1;
                break;
            case Registers.SER:
                ser = value & 1;
                break;
            case Registers.BAUDR:
                baudr = value & 0xFFFF;
                break;
            case Registers.TXFTLR:
                txftlr = value & 0xFF;
                break;
            case Registers.RXFTLR:
                rxftlr = value & 0xFF;
                break;
            case Registers.IMR:
                imr = value & 0x3F;
                UpdateIRQ();
                break;
            case Registers.DR0:
                if(ssienr == 0) break;
                txFifo.Enqueue(value);
                DoTransfer();
                break;
            case Registers.SPI_CTRLR0:
                spiCtrlr0 = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_SSI: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            ctrlr0    = 0x00070000;
            ctrlr1    = 0;
            ssienr    = 0;
            ser       = 0;
            baudr     = 0;
            txftlr    = 0;
            rxftlr    = 0;
            imr       = 0x3F;
            risr      = 0;
            spiCtrlr0 = 0x03000000;
            txFifo.Clear();
            rxFifo.Clear();
            IRQ.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private void DoTransfer()
        {
            while(txFifo.Count > 0)
            {
                txFifo.Dequeue();
                rxFifo.Enqueue(0xFF); /* dummy response */
            }
            risr |= RISR_RXFI;
            UpdateIRQ();
        }

        private uint BuildSR()
        {
            uint sr = SR_TFE; /* TX FIFO always logically empty after immediate transfer */
            sr |= SR_TFNF;   /* TX FIFO not full */
            if(rxFifo.Count > 0) sr |= SR_RFNE;
            if(rxFifo.Count >= FifoDepth) sr |= SR_RFF;
            return sr;
        }

        private void UpdateIRQ()
        {
            if((risr & imr) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            CTRLR0     = 0x00,
            CTRLR1     = 0x04,
            SSIENR     = 0x08,
            SER        = 0x10,
            BAUDR      = 0x14,
            TXFTLR     = 0x18,
            RXFTLR     = 0x1C,
            TXFLR      = 0x20,
            RXFLR      = 0x24,
            SR         = 0x28,
            IMR        = 0x2C,
            ISR        = 0x30,
            RISR       = 0x34,
            ICR        = 0x48,
            DR0        = 0x60,
            SPI_CTRLR0 = 0xF0,
        }

        private const uint SR_BUSY = (1u << 0);
        private const uint SR_TFNF = (1u << 1);
        private const uint SR_TFE  = (1u << 2);
        private const uint SR_RFNE = (1u << 3);
        private const uint SR_RFF  = (1u << 4);
        private const uint RISR_RXFI = (1u << 4);
        private const int FifoDepth  = 16;

        private uint ctrlr0, ctrlr1, ssienr, ser, baudr;
        private uint txftlr, rxftlr, imr, risr, spiCtrlr0;
        private readonly Queue<uint> txFifo;
        private readonly Queue<uint> rxFifo;
    }
}
