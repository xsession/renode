//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// RP2040 SPI — PL022-compatible SPI controller.
//
// Register map (32-bit, byte offsets):
//   0x000  SSPCR0   — Control register 0 (DSS, FRF, SPO, SPH, SCR)
//   0x004  SSPCR1   — Control register 1 (LBM, SSE, MS, SOD)
//   0x008  SSPDR    — Data register (read=RX, write=TX)
//   0x00C  SSPSR    — Status register (TFE, TNF, RNE, RFF, BSY)
//   0x010  SSPCPSR  — Clock prescale register
//   0x014  SSPIMSC  — Interrupt mask
//   0x018  SSPRIS   — Raw interrupt status
//   0x01C  SSPMIS   — Masked interrupt status
//   0x020  SSPICR   — Interrupt clear
//   0x024  SSPDMACR — DMA control
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class RP2040_SPI : IDoubleWordPeripheral, IKnownSize
    {
        public RP2040_SPI(IMachine machine)
        {
            IRQ = new GPIO();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.SSPCR0:  return cr0;
            case Registers.SSPCR1:  return cr1;
            case Registers.SSPDR:
                hasRx = false;
                UpdateIRQ();
                return rxBuf;
            case Registers.SSPSR:   return BuildSR();
            case Registers.SSPCPSR: return cpsr;
            case Registers.SSPIMSC: return imsc;
            case Registers.SSPRIS:  return ris;
            case Registers.SSPMIS:  return ris & imsc;
            case Registers.SSPICR:  return 0;
            case Registers.SSPDMACR: return dmacr;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_SPI: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.SSPCR0:
                cr0 = value & 0xFFFF;
                break;
            case Registers.SSPCR1:
                cr1 = value & 0x0F;
                break;
            case Registers.SSPDR:
                if((cr1 & CR1_SSE) == 0) break;
                txBuf = (ushort)(value & 0xFFFF);
                DoTransfer();
                break;
            case Registers.SSPCPSR:
                cpsr = value & 0xFF;
                break;
            case Registers.SSPIMSC:
                imsc = value & 0x0F;
                UpdateIRQ();
                break;
            case Registers.SSPICR:
                ris &= ~(value & 0x03);
                UpdateIRQ();
                break;
            case Registers.SSPDMACR:
                dmacr = value & 0x03;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "RP2040_SPI: Write 0x{0:X8} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            cr0   = 0;
            cr1   = 0;
            cpsr  = 0;
            imsc  = 0;
            ris   = 0;
            dmacr = 0;
            rxBuf = 0;
            txBuf = 0;
            hasRx = false;
            IRQ.Unset();
        }

        public long Size => 0x1000;
        public GPIO IRQ { get; }

        public event Action<ushort, ushort> TransferCompleted;

        private void DoTransfer()
        {
            rxBuf = 0xFFFF;
            hasRx = true;
            ris |= RIS_RX;
            UpdateIRQ();
            TransferCompleted?.Invoke(txBuf, (ushort)rxBuf);
        }

        private uint BuildSR()
        {
            uint sr = SR_TFE | SR_TNF; /* Tx always empty and not full */
            if(hasRx) sr |= SR_RNE;
            return sr;
        }

        private void UpdateIRQ()
        {
            if((ris & imsc) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            SSPCR0   = 0x000,
            SSPCR1   = 0x004,
            SSPDR    = 0x008,
            SSPSR    = 0x00C,
            SSPCPSR  = 0x010,
            SSPIMSC  = 0x014,
            SSPRIS   = 0x018,
            SSPMIS   = 0x01C,
            SSPICR   = 0x020,
            SSPDMACR = 0x024,
        }

        private const uint CR1_SSE = (1 << 1);
        private const uint SR_TFE  = (1 << 0);
        private const uint SR_TNF  = (1 << 1);
        private const uint SR_RNE  = (1 << 2);
        private const uint RIS_RX  = (1 << 2);

        private uint cr0, cr1, cpsr, imsc, ris, dmacr, rxBuf;
        private ushort txBuf;
        private bool hasRx;
    }
}
