//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 I2C — DW_apb_i2c (Synopsys DesignWare) compatible I2C controller.
//
// Register map (32-bit, byte offsets — key registers only):
//   0x00  IC_CON        — Control
//   0x04  IC_TAR        — Target address
//   0x10  IC_DATA_CMD   — Data / command (read/write)
//   0x14  IC_SS_SCL_HCNT — Standard speed SCL high count
//   0x18  IC_SS_SCL_LCNT — Standard speed SCL low count
//   0x1C  IC_FS_SCL_HCNT — Fast speed SCL high count
//   0x20  IC_FS_SCL_LCNT — Fast speed SCL low count
//   0x2C  IC_INTR_STAT  — Interrupt status (read-only)
//   0x30  IC_INTR_MASK  — Interrupt mask
//   0x34  IC_RAW_INTR_STAT — Raw interrupt status
//   0x38  IC_RX_TL      — Receive FIFO threshold
//   0x3C  IC_TX_TL      — Transmit FIFO threshold
//   0x40  IC_CLR_INTR   — Clear combined interrupt
//   0x6C  IC_ENABLE     — Enable register
//   0x70  IC_STATUS     — Status register
//   0x74  IC_TXFLR      — Transmit FIFO level
//   0x78  IC_RXFLR      — Receive FIFO level
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class RP2040_I2C : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_I2C(IMachine machine)
        {
            IRQ = new GPIO();
            rxFifo = new Queue<byte>();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.IC_CON:          return con;
            case Registers.IC_TAR:          return tar;
            case Registers.IC_DATA_CMD:     return ReadDataCmd();
            case Registers.IC_SS_SCL_HCNT:  return ssHcnt;
            case Registers.IC_SS_SCL_LCNT:  return ssLcnt;
            case Registers.IC_FS_SCL_HCNT:  return fsHcnt;
            case Registers.IC_FS_SCL_LCNT:  return fsLcnt;
            case Registers.IC_INTR_STAT:    return rawIntr & intrMask;
            case Registers.IC_INTR_MASK:    return intrMask;
            case Registers.IC_RAW_INTR_STAT: return rawIntr;
            case Registers.IC_RX_TL:        return rxTl;
            case Registers.IC_TX_TL:        return txTl;
            case Registers.IC_CLR_INTR:
                rawIntr = 0;
                IRQ.Unset();
                return 0;
            case Registers.IC_ENABLE:       return enable;
            case Registers.IC_STATUS:       return BuildStatus();
            case Registers.IC_TXFLR:        return 0;
            case Registers.IC_RXFLR:        return (uint)rxFifo.Count;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_I2C: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.IC_CON:
                con = value & 0x1FF;
                break;
            case Registers.IC_TAR:
                tar = value & 0x3FF;
                break;
            case Registers.IC_DATA_CMD:
                WriteDataCmd(value);
                break;
            case Registers.IC_SS_SCL_HCNT:
                ssHcnt = value & 0xFFFF;
                break;
            case Registers.IC_SS_SCL_LCNT:
                ssLcnt = value & 0xFFFF;
                break;
            case Registers.IC_FS_SCL_HCNT:
                fsHcnt = value & 0xFFFF;
                break;
            case Registers.IC_FS_SCL_LCNT:
                fsLcnt = value & 0xFFFF;
                break;
            case Registers.IC_INTR_MASK:
                intrMask = value & 0xFFF;
                UpdateIRQ();
                break;
            case Registers.IC_RX_TL:
                rxTl = value & 0xFF;
                break;
            case Registers.IC_TX_TL:
                txTl = value & 0xFF;
                break;
            case Registers.IC_ENABLE:
                enable = value & 0x03;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_I2C: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            con      = 0x7F; /* default: master, 7-bit, restart enabled, fast */
            tar      = 0x055;
            enable   = 0;
            intrMask = 0;
            rawIntr  = 0;
            ssHcnt   = 0x190;
            ssLcnt   = 0x1D6;
            fsHcnt   = 0x3C;
            fsLcnt   = 0x82;
            rxTl     = 0;
            txTl     = 0;
            rxFifo.Clear();
            IRQ.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        private uint ReadDataCmd()
        {
            if(rxFifo.Count == 0)
                return 0;
            var b = rxFifo.Dequeue();
            if(rxFifo.Count == 0)
                rawIntr &= ~INTR_RX_FULL;
            UpdateIRQ();
            return b;
        }

        private void WriteDataCmd(uint value)
        {
            if((enable & 1) == 0) return;

            bool isRead = (value & DATA_CMD_READ) != 0;
            if(isRead)
            {
                /* In a real device this would send out a read on the bus.
                   Here we provide a dummy 0xFF response. */
                rxFifo.Enqueue(0xFF);
                rawIntr |= INTR_RX_FULL;
                UpdateIRQ();
            }
            else
            {
                /* TX — data goes out; immediate completion in model */
                rawIntr |= INTR_TX_EMPTY;
                UpdateIRQ();
            }
        }

        private uint BuildStatus()
        {
            uint st = STATUS_TFE | STATUS_TFNF; /* Tx always ready */
            if(rxFifo.Count > 0)
                st |= STATUS_RFNE;
            if((enable & 1) != 0)
                st |= STATUS_ACTIVITY;
            return st;
        }

        private void UpdateIRQ()
        {
            if((rawIntr & intrMask) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            IC_CON            = 0x00,
            IC_TAR            = 0x04,
            IC_DATA_CMD       = 0x10,
            IC_SS_SCL_HCNT    = 0x14,
            IC_SS_SCL_LCNT    = 0x18,
            IC_FS_SCL_HCNT    = 0x1C,
            IC_FS_SCL_LCNT    = 0x20,
            IC_INTR_STAT      = 0x2C,
            IC_INTR_MASK      = 0x30,
            IC_RAW_INTR_STAT  = 0x34,
            IC_RX_TL          = 0x38,
            IC_TX_TL          = 0x3C,
            IC_CLR_INTR       = 0x40,
            IC_ENABLE         = 0x6C,
            IC_STATUS         = 0x70,
            IC_TXFLR          = 0x74,
            IC_RXFLR          = 0x78,
        }

        private const uint DATA_CMD_READ  = (1 << 8);
        private const uint INTR_RX_FULL   = (1 << 2);
        private const uint INTR_TX_EMPTY  = (1 << 4);
        private const uint STATUS_ACTIVITY = (1 << 0);
        private const uint STATUS_TFNF    = (1 << 1);
        private const uint STATUS_TFE     = (1 << 2);
        private const uint STATUS_RFNE    = (1 << 3);

        private uint con, tar, enable, intrMask, rawIntr;
        private uint ssHcnt, ssLcnt, fsHcnt, fsLcnt, rxTl, txTl;
        private readonly Queue<byte> rxFifo;
    }
}
