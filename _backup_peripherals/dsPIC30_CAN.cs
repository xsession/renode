//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC30 CAN peripheral (CAN 2.0B module — C1 module).
//
// Register map (word-wide, dsPIC30F Family Reference Manual, Section 23):
//   0x00  C1RXF0SID  — Receive filter 0 SID
//   0x04  C1RXF1SID  — Receive filter 1 SID
//   ...   (filters 0-5 at 4-byte stride)
//   0x20  C1RXM0SID  — Receive mask 0 SID
//   0x24  C1RXM1SID  — Receive mask 1 SID
//   0x30  C1CTRL     — CAN control (CANCAP, CANCKS, CSIDL, ABAT, REQOP, OPMODE)
//   0x32  C1CFG1     — CAN baud rate config 1 (SJW, BRP)
//   0x34  C1CFG2     — CAN baud rate config 2 (WAKFIL, SEG2PH, SEG2PHTS, SAM, SEG1PH, PRSEG)
//   0x36  C1INTF     — CAN interrupt flag (ERRIF, WAKIF, IVRIF, FIFOIF, RXBnIF, TXBnIF)
//   0x38  C1INTE     — CAN interrupt enable
//   0x3A  C1EC       — CAN error counter (TEC:REC in one word)
//   0x40  C1RXB0CON  — RX Buffer 0 control
//   0x42  C1RXB0SID  — RX Buffer 0 SID
//   0x44  C1RXB0EID  — RX Buffer 0 EID
//   0x46  C1RXB0DLC  — RX Buffer 0 DLC
//   0x48  C1RXB0D0   — RX Buffer 0 Data byte 0-1
//   0x4A  C1RXB0D2   — RX Buffer 0 Data byte 2-3
//   0x4C  C1RXB0D4   — RX Buffer 0 Data byte 4-5
//   0x4E  C1RXB0D6   — RX Buffer 0 Data byte 6-7
//   0x50  C1RXB1CON  — RX Buffer 1 control (same layout)
//   ...
//   0x60  C1TXB0CON  — TX Buffer 0 control
//   0x62  C1TXB0SID  — TX Buffer 0 SID
//   0x64  C1TXB0EID  — TX Buffer 0 EID
//   0x66  C1TXB0DLC  — TX Buffer 0 DLC
//   0x68  C1TXB0D0–D6  — TX Buffer 0 Data
//   0x70  C1TXB1CON  — TX Buffer 1 (same layout)
//   0x80  C1TXB2CON  — TX Buffer 2 (same layout)
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.CAN
{
    public class dsPIC30_CAN : IWordPeripheral, IKnownSize
    {
        public dsPIC30_CAN(IMachine machine)
        {
            IRQ = new GPIO();
            rxFilters = new ushort[6];
            rxMasks   = new ushort[2];
            bufRam    = new ushort[BufferRamSize];
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            /* RX Filters 0-5 (stride 4, at 0x00-0x14) */
            if(offset >= 0x00 && offset < 0x18 && (offset % 4) == 0)
                return rxFilters[(int)(offset / 4)];

            /* RX Masks (0x20, 0x24) */
            if(offset == 0x20) return rxMasks[0];
            if(offset == 0x24) return rxMasks[1];

            /* Buffer RAM: RXB0 at 0x40, RXB1 at 0x50, TXB0-2 at 0x60/0x70/0x80 */
            if(offset >= 0x40 && offset < 0x90)
            {
                int idx = (int)(offset - 0x40) / 2;
                if(idx < BufferRamSize) return bufRam[idx];
                return 0;
            }

            switch((Registers)offset)
            {
            case Registers.C1CTRL: return ctrl;
            case Registers.C1CFG1: return cfg1;
            case Registers.C1CFG2: return cfg2;
            case Registers.C1INTF: return intf;
            case Registers.C1INTE: return inte;
            case Registers.C1EC:   return ec;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_CAN: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            /* RX Filters */
            if(offset >= 0x00 && offset < 0x18 && (offset % 4) == 0)
            {
                rxFilters[(int)(offset / 4)] = value;
                return;
            }

            /* RX Masks */
            if(offset == 0x20) { rxMasks[0] = value; return; }
            if(offset == 0x24) { rxMasks[1] = value; return; }

            /* Buffer RAM */
            if(offset >= 0x40 && offset < 0x90)
            {
                int idx = (int)(offset - 0x40) / 2;
                if(idx < BufferRamSize) bufRam[idx] = value;

                /* Check TX request — TXBnCON bit 3 (TXREQ) */
                if(offset == 0x60 || offset == 0x70 || offset == 0x80)
                {
                    if((value & TXBCON_TXREQ) != 0)
                        ProcessTransmit((int)(offset - 0x60) / 0x10);
                }
                return;
            }

            switch((Registers)offset)
            {
            case Registers.C1CTRL:
                ctrl = value;
                break;
            case Registers.C1CFG1:
                cfg1 = value;
                break;
            case Registers.C1CFG2:
                cfg2 = value;
                break;
            case Registers.C1INTF:
                /* W1C for interrupt flags */
                intf &= (ushort)~value;
                UpdateIRQ();
                break;
            case Registers.C1INTE:
                inte = value;
                UpdateIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_CAN: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        /// <summary>Inject a standard CAN frame into RX buffer 0.</summary>
        public void InjectFrame(ushort sid, byte[] data, int dlc)
        {
            /* RXB0 starts at bufRam index 0 (offset 0x40) */
            bufRam[0] = RXBCON_RXFUL; /* CON: RXFUL set */
            bufRam[1] = (ushort)((sid & 0x7FF) << 2);  /* SID */
            bufRam[2] = 0; /* EID */
            bufRam[3] = (ushort)(dlc & 0x0F); /* DLC */
            for(int i = 0; i < Math.Min(dlc, 8); i++)
            {
                int wordIdx = 4 + i / 2;
                if(i % 2 == 0)
                    bufRam[wordIdx] = (ushort)((bufRam[wordIdx] & 0xFF00) | data[i]);
                else
                    bufRam[wordIdx] = (ushort)((bufRam[wordIdx] & 0x00FF) | (data[i] << 8));
            }

            intf |= INTF_RXB0IF;
            UpdateIRQ();
        }

        public void Reset()
        {
            ctrl = CTRL_OPMODE_CONFIG; /* start in configuration mode */
            cfg1 = 0;
            cfg2 = 0;
            intf = 0;
            inte = 0;
            ec   = 0;
            for(int i = 0; i < 6; i++) rxFilters[i] = 0;
            for(int i = 0; i < 2; i++) rxMasks[i] = 0;
            for(int i = 0; i < BufferRamSize; i++) bufRam[i] = 0;
            IRQ.Unset();
        }

        public long Size => 0x90;
        public GPIO IRQ { get; }

        public event Action<int> FrameTransmitted;

        private void ProcessTransmit(int txBuf)
        {
            /* Immediate TX completion */
            int conIdx = txBuf * 8; /* TXBn base in bufRam (each buffer = 8 words, starting at idx 16) */
            int absIdx = 16 + conIdx; /* RXB0=0, RXB1=8, TXB0=16, TXB1=24, TXB2=32 */
            if(absIdx < BufferRamSize)
                bufRam[absIdx] &= unchecked((ushort)~TXBCON_TXREQ);

            intf |= (ushort)(INTF_TXB0IF << txBuf);
            UpdateIRQ();
            FrameTransmitted?.Invoke(txBuf);
        }

        private void UpdateIRQ()
        {
            if((intf & inte) != 0)
                IRQ.Set();
            else
                IRQ.Unset();
        }

        private enum Registers : long
        {
            C1CTRL = 0x30,
            C1CFG1 = 0x32,
            C1CFG2 = 0x34,
            C1INTF = 0x36,
            C1INTE = 0x38,
            C1EC   = 0x3A,
        }

        private const ushort CTRL_OPMODE_CONFIG = (0x04 << 5); /* OPMODE = Configuration */
        private const ushort TXBCON_TXREQ  = (1 << 3);
        private const ushort RXBCON_RXFUL  = (1 << 7);
        private const ushort INTF_RXB0IF   = (1 << 0);
        private const ushort INTF_TXB0IF   = (1 << 2);
        private const int BufferRamSize    = 40; /* 5 buffers × 8 words */

        private ushort ctrl, cfg1, cfg2, intf, inte, ec;
        private readonly ushort[] rxFilters;
        private readonly ushort[] rxMasks;
        private readonly ushort[] bufRam;
    }
}
