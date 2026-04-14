//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC33 ECAN (Enhanced CAN) peripheral.
//
// The ECAN module on dsPIC33F/dsPIC33E/dsPIC33C provides CAN 2.0B with DMA integration,
// up to 32 message buffers in DMA RAM, and acceptance filter / mask sets.
//
// Register map (word-wide, byte offsets — key registers):
//   0x00  C1CTRL1    — CAN control 1 (CANCAP, CSIDL, ABAT, CANCKS, REQOP, OPMODE, WIN)
//   0x02  C1CTRL2    — CAN control 2 (DNCNT)
//   0x04  C1VEC      — CAN interrupt code vector
//   0x06  C1FCTRL    — FIFO control (DMABS, FSA)
//   0x08  C1FIFO     — FIFO status (FNRB, FBP)
//   0x0A  C1INTF     — Interrupt flag (TBIF, RBIF, RBOVIF, FIFOIF, ERRIF, WAKIF, IVRIF)
//   0x0C  C1INTE     — Interrupt enable
//   0x0E  C1EC       — Error counter (TEC[7:0] : REC[7:0])
//   0x10  C1CFG1     — Baud rate config 1 (SJW, BRP)
//   0x12  C1CFG2     — Baud rate config 2 (WAKFIL, SEG2PH, SEG2PHTS, SAM, SEG1PH, PRSEG)
//
//   Acceptance filters (0x20–0x5E):
//     C1RXFnSID at 0x20 + n*4 (n = 0-15)
//     C1RXFnEID at 0x22 + n*4
//
//   Acceptance masks (0x60-0x6E):
//     C1RXMnSID at 0x60 + n*4 (n = 0-2)
//     C1RXMnEID at 0x62 + n*4
//
//   Filter enable / select:
//     0x70  C1FEN1     — Filter enable
//     0x74  C1BUFPNT1-4  — Buffer pointer (which buffer each filter maps to)
//
//   TX/RX control:
//     0x80  C1TR01CON  — TX/RX buffer 0/1 control (TXEN, TXABT, TXLARB, TXERR, TXREQ, RTREN)
//     0x82  C1TR23CON  — TX/RX buffer 2/3 control
//     0x84  C1TR45CON  — TX/RX buffer 4/5 control
//     0x86  C1TR67CON  — TX/RX buffer 6/7 control
//
//   Message buffers are in DMA RAM, addressed via DMA. This model provides a simplified
//   8-buffer register view at offsets 0xA0-0xFE.
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.CAN
{
    public class dsPIC33_ECAN : IWordPeripheral, IKnownSize
    {
        public dsPIC33_ECAN(IMachine machine)
        {
            IRQ = new GPIO();
            filters     = new ushort[MaxFilters * 2]; /* SID + EID per filter */
            masks       = new ushort[MaxMasks * 2];
            trCon       = new ushort[4]; /* TR01CON - TR67CON */
            bufPnt      = new ushort[4]; /* BUFPNT1-4 */
            msgBufRam   = new ushort[MsgBufCount * MsgBufWordSize];
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            /* Acceptance filters: 0x20–0x5F */
            if(offset >= 0x20 && offset < 0x60)
            {
                int idx = (int)(offset - 0x20) / 2;
                if(idx < filters.Length) return filters[idx];
                return 0;
            }

            /* Acceptance masks: 0x60–0x6D */
            if(offset >= 0x60 && offset < 0x6E)
            {
                int idx = (int)(offset - 0x60) / 2;
                if(idx < masks.Length) return masks[idx];
                return 0;
            }

            /* Buffer pointers: 0x74–0x7A */
            if(offset >= 0x74 && offset < 0x7C)
            {
                int idx = (int)(offset - 0x74) / 2;
                if(idx < bufPnt.Length) return bufPnt[idx];
                return 0;
            }

            /* TX/RX control: 0x80–0x86 */
            if(offset >= 0x80 && offset < 0x88)
            {
                int idx = (int)(offset - 0x80) / 2;
                return trCon[idx];
            }

            /* Message buffer RAM: 0xA0–0xFE */
            if(offset >= 0xA0 && offset < 0xA0 + MsgBufCount * MsgBufWordSize * 2)
            {
                int idx = (int)(offset - 0xA0) / 2;
                if(idx < msgBufRam.Length) return msgBufRam[idx];
                return 0;
            }

            switch((Registers)offset)
            {
            case Registers.C1CTRL1: return ctrl1;
            case Registers.C1CTRL2: return ctrl2;
            case Registers.C1VEC:   return vec;
            case Registers.C1FCTRL: return fctrl;
            case Registers.C1FIFO:  return fifo;
            case Registers.C1INTF:  return intf;
            case Registers.C1INTE:  return inte;
            case Registers.C1EC:    return ec;
            case Registers.C1CFG1:  return cfg1;
            case Registers.C1CFG2:  return cfg2;
            case Registers.C1FEN1:  return fen1;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_ECAN: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            /* Acceptance filters */
            if(offset >= 0x20 && offset < 0x60)
            {
                int idx = (int)(offset - 0x20) / 2;
                if(idx < filters.Length) filters[idx] = value;
                return;
            }

            /* Acceptance masks */
            if(offset >= 0x60 && offset < 0x6E)
            {
                int idx = (int)(offset - 0x60) / 2;
                if(idx < masks.Length) masks[idx] = value;
                return;
            }

            /* Buffer pointers */
            if(offset >= 0x74 && offset < 0x7C)
            {
                int idx = (int)(offset - 0x74) / 2;
                if(idx < bufPnt.Length) bufPnt[idx] = value;
                return;
            }

            /* TX/RX control */
            if(offset >= 0x80 && offset < 0x88)
            {
                int idx = (int)(offset - 0x80) / 2;
                trCon[idx] = value;
                CheckTransmit(idx);
                return;
            }

            /* Message buffer RAM */
            if(offset >= 0xA0 && offset < 0xA0 + MsgBufCount * MsgBufWordSize * 2)
            {
                int idx = (int)(offset - 0xA0) / 2;
                if(idx < msgBufRam.Length) msgBufRam[idx] = value;
                return;
            }

            switch((Registers)offset)
            {
            case Registers.C1CTRL1:
                ctrl1 = value;
                break;
            case Registers.C1CTRL2:
                ctrl2 = value;
                break;
            case Registers.C1FCTRL:
                fctrl = value;
                break;
            case Registers.C1INTF:
                intf &= (ushort)~value; /* W1C */
                UpdateIRQ();
                break;
            case Registers.C1INTE:
                inte = value;
                UpdateIRQ();
                break;
            case Registers.C1CFG1:
                cfg1 = value;
                break;
            case Registers.C1CFG2:
                cfg2 = value;
                break;
            case Registers.C1FEN1:
                fen1 = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC33_ECAN: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        /// <summary>Inject a CAN frame into message buffer 0 (RX).</summary>
        public void InjectFrame(ushort sid, byte[] data, int dlc)
        {
            /* Buffer 0 (= words 0-7 in msgBufRam) */
            msgBufRam[0] = (ushort)((sid & 0x7FF) << 2); /* SID */
            msgBufRam[1] = 0; /* EID high */
            msgBufRam[2] = (ushort)(dlc & 0x0F);    /* DLC + EID low */

            for(int i = 0; i < Math.Min(dlc, 8); i++)
            {
                int wordIdx = 3 + i / 2;
                if(i % 2 == 0)
                    msgBufRam[wordIdx] = (ushort)((msgBufRam[wordIdx] & 0xFF00) | data[i]);
                else
                    msgBufRam[wordIdx] = (ushort)((msgBufRam[wordIdx] & 0x00FF) | (data[i] << 8));
            }

            intf |= INTF_RBIF;
            UpdateIRQ();
        }

        public void Reset()
        {
            ctrl1 = CTRL1_OPMODE_CONFIG;
            ctrl2 = 0;
            vec   = 0x40; /* default: no pending interrupt */
            fctrl = 0;
            fifo  = 0;
            intf  = 0;
            inte  = 0;
            ec    = 0;
            cfg1  = 0;
            cfg2  = 0;
            fen1  = 0;
            for(int i = 0; i < filters.Length; i++)   filters[i] = 0;
            for(int i = 0; i < masks.Length; i++)     masks[i] = 0;
            for(int i = 0; i < trCon.Length; i++)     trCon[i] = 0;
            for(int i = 0; i < bufPnt.Length; i++)    bufPnt[i] = 0;
            for(int i = 0; i < msgBufRam.Length; i++) msgBufRam[i] = 0;
            IRQ.Unset();
        }

        public long Size => 0x100;
        public GPIO IRQ { get; }

        public event Action<int> FrameTransmitted;

        private void CheckTransmit(int regIdx)
        {
            /* Each TRxxCON register controls 2 buffers (low byte = even, high byte = odd) */
            for(int sub = 0; sub < 2; sub++)
            {
                int bufNum = regIdx * 2 + sub;
                ushort conVal = (sub == 0) 
                    ? (ushort)(trCon[regIdx] & 0xFF) 
                    : (ushort)((trCon[regIdx] >> 8) & 0xFF);

                bool txEn  = (conVal & TRCON_TXEN) != 0;
                bool txReq = (conVal & TRCON_TXREQ) != 0;
                if(txEn && txReq)
                {
                    /* Immediate completion */
                    if(sub == 0)
                        trCon[regIdx] &= unchecked((ushort)~TRCON_TXREQ);
                    else
                        trCon[regIdx] &= unchecked((ushort)~(TRCON_TXREQ << 8));

                    intf |= INTF_TBIF;
                    UpdateIRQ();
                    FrameTransmitted?.Invoke(bufNum);
                }
            }
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
            C1CTRL1 = 0x00,
            C1CTRL2 = 0x02,
            C1VEC   = 0x04,
            C1FCTRL = 0x06,
            C1FIFO  = 0x08,
            C1INTF  = 0x0A,
            C1INTE  = 0x0C,
            C1EC    = 0x0E,
            C1CFG1  = 0x10,
            C1CFG2  = 0x12,
            C1FEN1  = 0x70,
        }

        private const ushort CTRL1_OPMODE_CONFIG = (0x04 << 5);
        private const ushort INTF_TBIF  = (1 << 0);
        private const ushort INTF_RBIF  = (1 << 1);
        private const ushort TRCON_TXEN  = (1 << 3);
        private const ushort TRCON_TXREQ = (1 << 1);
        private const int MaxFilters    = 16;
        private const int MaxMasks      = 3;
        private const int MsgBufCount   = 8;
        private const int MsgBufWordSize = 8; /* 8 × 16-bit words per buffer */

        private ushort ctrl1, ctrl2, vec, fctrl, fifo, intf, inte, ec, cfg1, cfg2, fen1;
        private readonly ushort[] filters;
        private readonly ushort[] masks;
        private readonly ushort[] trCon;
        private readonly ushort[] bufPnt;
        private readonly ushort[] msgBufRam;
    }
}
