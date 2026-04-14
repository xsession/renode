//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// TMS320C28x eCAN (Enhanced Controller Area Network) peripheral.
//
// Mailbox-based CAN 2.0B controller found on F2806x / F2837xD devices.
//
// Register map (16-bit word addresses, byte offsets — key registers):
//   0x00  CANME     — Mailbox Enable (32-bit via low/high)
//   0x02  CANMEH
//   0x04  CANMD     — Mailbox Direction (0=TX, 1=RX)
//   0x06  CANMDH
//   0x08  CANTRS    — Transmit Request Set
//   0x0A  CANTRSH
//   0x0C  CANTRR    — Transmit Request Reset
//   0x0E  CANTRRH
//   0x10  CANTA     — Transmit Acknowledge
//   0x12  CANTAH
//   0x14  CANAA     — Abort Acknowledge
//   0x16  CANAAH
//   0x18  CANRMP    — Receive Message Pending
//   0x1A  CANRMPH
//   0x1C  CANRML    — Receive Message Lost
//   0x1E  CANRMLH
//   0x20  CANRFP    — Remote Frame Pending
//   0x22  CANRFPH
//   0x28  CANMC     — Master Control (CCE, PDR, DBO, WUBA, CDR, ABO, STM, SRES, MBNR)
//   0x2A  CANMCH
//   0x2C  CANBTC    — Bit Timing Configuration
//   0x2E  CANBTCH
//   0x30  CANES     — Error/Status (FE, BE, SA1, EW, EP, BO, ACKE, SE, CRCE, etc.)
//   0x32  CANESH
//   0x34  CANTEC    — Transmit Error Counter
//   0x36  CANREC    — Receive Error Counter
//   0x38  CANGIF0   — Global Interrupt Flag 0
//   0x3A  CANGIF0H
//   0x3C  CANGIM    — Global Interrupt Mask
//   0x3E  CANGIMH
//   0x40  CANGIF1   — Global Interrupt Flag 1
//   0x42  CANGIF1H
//   0x48  CANMIM    — Mailbox Interrupt Mask
//   0x4A  CANMIMH
//   0x4C  CANMIL    — Mailbox Interrupt Level
//   0x4E  CANMILH
//   0x50  CANOPC    — Overwrite Protection Control
//   0x52  CANOPCH
//   0x58  CANTIOC   — TX I/O Control
//   0x5A  CANTIOCH
//   0x5C  CANRIOC   — RX I/O Control
//   0x5E  CANRIOCH
//
// Mailbox RAM starts at offset 0x100, 32 mailboxes × 4 words each:
//   Mailbox n (offset 0x100 + n*0x10):
//     +0x00  MSGID  (low)   — Message ID
//     +0x02  MSGIDH
//     +0x04  MSGCTRL(low)   — Message Control (DLC, RTR, etc.)
//     +0x06  MSGCTRLH
//     +0x08  CANMDL (low)   — Data Low
//     +0x0A  CANMDLH
//     +0x0C  CANMDH (low)   — Data High
//     +0x0E  CANMDHH
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.CAN
{
    public class C2000_eCAN : IWordPeripheral, IKnownSize
    {
        public C2000_eCAN(IMachine machine)
        {
            IRQ0 = new GPIO();
            IRQ1 = new GPIO();
            mailboxRam = new ushort[MailboxCount * MailboxWordSize];
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            /* Mailbox RAM region */
            if(offset >= MailboxBase && offset < MailboxBase + MailboxCount * MailboxByteSize)
            {
                int idx = (int)(offset - MailboxBase) / 2;
                return mailboxRam[idx];
            }

            switch((Registers)offset)
            {
            case Registers.CANME:    return (ushort)(canme & 0xFFFF);
            case Registers.CANMEH:   return (ushort)(canme >> 16);
            case Registers.CANMD:    return (ushort)(canmd & 0xFFFF);
            case Registers.CANMDH:   return (ushort)(canmd >> 16);
            case Registers.CANTRS:   return (ushort)(cantrs & 0xFFFF);
            case Registers.CANTRSH:  return (ushort)(cantrs >> 16);
            case Registers.CANTA:    return (ushort)(canta & 0xFFFF);
            case Registers.CANTAH:   return (ushort)(canta >> 16);
            case Registers.CANAA:    return (ushort)(canaa & 0xFFFF);
            case Registers.CANAAH:   return (ushort)(canaa >> 16);
            case Registers.CANRMP:   return (ushort)(canrmp & 0xFFFF);
            case Registers.CANRMPH:  return (ushort)(canrmp >> 16);
            case Registers.CANRML:   return (ushort)(canrml & 0xFFFF);
            case Registers.CANRMLH:  return (ushort)(canrml >> 16);
            case Registers.CANMC:    return (ushort)(canmc & 0xFFFF);
            case Registers.CANMCH:   return (ushort)(canmc >> 16);
            case Registers.CANBTC:   return (ushort)(canbtc & 0xFFFF);
            case Registers.CANBTCH:  return (ushort)(canbtc >> 16);
            case Registers.CANES:    return (ushort)(canes & 0xFFFF);
            case Registers.CANESH:   return (ushort)(canes >> 16);
            case Registers.CANTEC:   return cantec;
            case Registers.CANREC:   return canrec;
            case Registers.CANGIF0:  return (ushort)(cangif0 & 0xFFFF);
            case Registers.CANGIF0H: return (ushort)(cangif0 >> 16);
            case Registers.CANGIM:   return (ushort)(cangim & 0xFFFF);
            case Registers.CANGIMH:  return (ushort)(cangim >> 16);
            case Registers.CANGIF1:  return (ushort)(cangif1 & 0xFFFF);
            case Registers.CANGIF1H: return (ushort)(cangif1 >> 16);
            case Registers.CANMIM:   return (ushort)(canmim & 0xFFFF);
            case Registers.CANMIMH:  return (ushort)(canmim >> 16);
            case Registers.CANMIL:   return (ushort)(canmil & 0xFFFF);
            case Registers.CANMILH:  return (ushort)(canmil >> 16);
            case Registers.CANOPC:   return (ushort)(canopc & 0xFFFF);
            case Registers.CANOPCH:  return (ushort)(canopc >> 16);
            case Registers.CANTIOC:  return cantioc;
            case Registers.CANRIOC:  return canrioc;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_eCAN: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            /* Mailbox RAM region */
            if(offset >= MailboxBase && offset < MailboxBase + MailboxCount * MailboxByteSize)
            {
                int idx = (int)(offset - MailboxBase) / 2;
                mailboxRam[idx] = value;
                return;
            }

            switch((Registers)offset)
            {
            case Registers.CANME:    canme = (canme & 0xFFFF0000u) | value; break;
            case Registers.CANMEH:   canme = (canme & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANMD:    canmd = (canmd & 0xFFFF0000u) | value; break;
            case Registers.CANMDH:   canmd = (canmd & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANTRS:
                cantrs |= value;
                ProcessTransmit();
                break;
            case Registers.CANTRSH:
                cantrs |= (uint)value << 16;
                ProcessTransmit();
                break;
            case Registers.CANTRR:   cantrs &= ~(uint)value; break;
            case Registers.CANTRRH:  cantrs &= ~((uint)value << 16); break;
            case Registers.CANTA:    canta &= ~(uint)value; break;
            case Registers.CANTAH:   canta &= ~((uint)value << 16); break;
            case Registers.CANAA:    canaa &= ~(uint)value; break;
            case Registers.CANAAH:   canaa &= ~((uint)value << 16); break;
            case Registers.CANRMP:   canrmp &= ~(uint)value; break;
            case Registers.CANRMPH:  canrmp &= ~((uint)value << 16); break;
            case Registers.CANRML:   canrml &= ~(uint)value; break;
            case Registers.CANRMLH:  canrml &= ~((uint)value << 16); break;
            case Registers.CANMC:    canmc = (canmc & 0xFFFF0000u) | value; break;
            case Registers.CANMCH:   canmc = (canmc & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANBTC:   canbtc = (canbtc & 0xFFFF0000u) | value; break;
            case Registers.CANBTCH:  canbtc = (canbtc & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANES:
                /* W1C for error flags */
                canes &= ~(uint)(value & 0xFF);
                break;
            case Registers.CANGIF0:  cangif0 &= ~(uint)value; UpdateIRQ(); break;
            case Registers.CANGIF0H: cangif0 &= ~((uint)value << 16); UpdateIRQ(); break;
            case Registers.CANGIM:   cangim = (cangim & 0xFFFF0000u) | value; UpdateIRQ(); break;
            case Registers.CANGIMH:  cangim = (cangim & 0x0000FFFFu) | ((uint)value << 16); UpdateIRQ(); break;
            case Registers.CANGIF1:  cangif1 &= ~(uint)value; UpdateIRQ(); break;
            case Registers.CANGIF1H: cangif1 &= ~((uint)value << 16); UpdateIRQ(); break;
            case Registers.CANMIM:   canmim = (canmim & 0xFFFF0000u) | value; break;
            case Registers.CANMIMH:  canmim = (canmim & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANMIL:   canmil = (canmil & 0xFFFF0000u) | value; break;
            case Registers.CANMILH:  canmil = (canmil & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANOPC:   canopc = (canopc & 0xFFFF0000u) | value; break;
            case Registers.CANOPCH:  canopc = (canopc & 0x0000FFFFu) | ((uint)value << 16); break;
            case Registers.CANTIOC:  cantioc = value; break;
            case Registers.CANRIOC:  canrioc = value; break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_eCAN: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        /// <summary>Inject a CAN frame into a receive mailbox.</summary>
        public void InjectFrame(int mailbox, uint msgId, byte[] data, int dlc)
        {
            if(mailbox < 0 || mailbox >= MailboxCount) return;
            if((canme & (1u << mailbox)) == 0) return;
            if((canmd & (1u << mailbox)) == 0) return; /* must be RX */

            int baseIdx = mailbox * MailboxWordSize;
            mailboxRam[baseIdx + 0] = (ushort)(msgId & 0xFFFF);
            mailboxRam[baseIdx + 1] = (ushort)(msgId >> 16);
            mailboxRam[baseIdx + 2] = (ushort)(dlc & 0x0F);
            mailboxRam[baseIdx + 3] = 0;

            for(int i = 0; i < Math.Min(dlc, 8); i++)
            {
                int wordIdx = 4 + i / 2;
                if(i % 2 == 0)
                    mailboxRam[baseIdx + wordIdx] = (ushort)((mailboxRam[baseIdx + wordIdx] & 0xFF00) | data[i]);
                else
                    mailboxRam[baseIdx + wordIdx] = (ushort)((mailboxRam[baseIdx + wordIdx] & 0x00FF) | (data[i] << 8));
            }

            canrmp |= (1u << mailbox);
            cangif0 |= GIF_GMIF;
            UpdateIRQ();
        }

        public void Reset()
        {
            canme  = 0;
            canmd  = 0;
            cantrs = 0;
            canta  = 0;
            canaa  = 0;
            canrmp = 0;
            canrml = 0;
            canmc  = 0;
            canbtc = 0;
            canes  = 0;
            cantec = 0;
            canrec = 0;
            cangif0 = 0;
            cangim  = 0;
            cangif1 = 0;
            canmim  = 0;
            canmil  = 0;
            canopc  = 0;
            cantioc = 0;
            canrioc = 0;
            for(int i = 0; i < mailboxRam.Length; i++) mailboxRam[i] = 0;
            IRQ0.Unset();
            IRQ1.Unset();
        }

        public long Size => 0x300;
        public GPIO IRQ0 { get; }
        public GPIO IRQ1 { get; }

        private void ProcessTransmit()
        {
            for(int mb = 0; mb < MailboxCount; mb++)
            {
                if((cantrs & (1u << mb)) == 0) continue;
                if((canme & (1u << mb)) == 0) continue;
                if((canmd & (1u << mb)) != 0) continue; /* skip RX mailboxes */

                /* Immediate TX completion in model */
                cantrs &= ~(1u << mb);
                canta  |= (1u << mb);
                cangif0 |= GIF_GMIF;

                FrameTransmitted?.Invoke(mb);
            }
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            /* Simplified: GIF0 → IRQ0, GIF1 → IRQ1 */
            if((cangif0 & cangim & 0xFFFF) != 0)
                IRQ0.Set();
            else
                IRQ0.Unset();

            if((cangif1 & (cangim >> 16) & 0xFFFF) != 0)
                IRQ1.Set();
            else
                IRQ1.Unset();
        }

        public event Action<int> FrameTransmitted;

        private enum Registers : long
        {
            CANME    = 0x00, CANMEH   = 0x02,
            CANMD    = 0x04, CANMDH   = 0x06,
            CANTRS   = 0x08, CANTRSH  = 0x0A,
            CANTRR   = 0x0C, CANTRRH  = 0x0E,
            CANTA    = 0x10, CANTAH   = 0x12,
            CANAA    = 0x14, CANAAH   = 0x16,
            CANRMP   = 0x18, CANRMPH  = 0x1A,
            CANRML   = 0x1C, CANRMLH  = 0x1E,
            CANRFP   = 0x20, CANRFPH  = 0x22,
            CANMC    = 0x28, CANMCH   = 0x2A,
            CANBTC   = 0x2C, CANBTCH  = 0x2E,
            CANES    = 0x30, CANESH   = 0x32,
            CANTEC   = 0x34, CANREC   = 0x36,
            CANGIF0  = 0x38, CANGIF0H = 0x3A,
            CANGIM   = 0x3C, CANGIMH  = 0x3E,
            CANGIF1  = 0x40, CANGIF1H = 0x42,
            CANMIM   = 0x48, CANMIMH  = 0x4A,
            CANMIL   = 0x4C, CANMILH  = 0x4E,
            CANOPC   = 0x50, CANOPCH  = 0x52,
            CANTIOC  = 0x58, CANTIOCH = 0x5A,
            CANRIOC  = 0x5C, CANRIOCH = 0x5E,
        }

        private const uint GIF_GMIF = (1u << 15);
        private const int MailboxCount    = 32;
        private const int MailboxWordSize = 8; /* 8 × 16-bit words per mailbox */
        private const int MailboxByteSize = 16;
        private const int MailboxBase     = 0x100;

        private uint canme, canmd, cantrs, canta, canaa, canrmp, canrml;
        private uint canmc, canbtc, canes;
        private ushort cantec, canrec;
        private uint cangif0, cangim, cangif1;
        private uint canmim, canmil, canopc;
        private ushort cantioc, canrioc;
        private readonly ushort[] mailboxRam;
    }
}
