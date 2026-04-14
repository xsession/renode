//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x Flash/OTP Controller.
// Controls flash erase, program, and read wait-state configuration.
//
// Register map (from base, typically 0x000A80):
//   0x00  FOPT       — Flash option register
//   0x02  FPWR       — Flash power modes
//   0x04  FSTATUS    — Flash status
//   0x06  FSTDBYWAIT — Flash standby wait
//   0x08  FACTIVEWAIT — Flash active wait
//   0x0A  FBANKWAIT  — Flash bank read wait-states
//   0x0C  FOTPWAIT   — OTP read wait-states
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class C2000_Flash : IWordPeripheral, IKnownSize
    {
        public C2000_Flash()
        {
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.FOPT:       return fopt;
            case Registers.FPWR:       return fpwr;
            case Registers.FSTATUS:    return fstatus;
            case Registers.FSTDBYWAIT: return fstdbywait;
            case Registers.FACTIVEWAIT:return factivewait;
            case Registers.FBANKWAIT:  return fbankwait;
            case Registers.FOTPWAIT:   return fotpwait;
            default:
                this.Log(LogLevel.Warning, "C2000_Flash: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.FOPT:        fopt = value; break;
            case Registers.FPWR:        fpwr = (ushort)(value & 0x03); break;
            case Registers.FSTDBYWAIT:  fstdbywait = value; break;
            case Registers.FACTIVEWAIT: factivewait = value; break;
            case Registers.FBANKWAIT:   fbankwait = (ushort)(value & 0x0F); break;
            case Registers.FOTPWAIT:    fotpwait = (ushort)(value & 0x1F); break;
            default:
                this.Log(LogLevel.Warning, "C2000_Flash: Write 0x{0:X} to offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            fopt = 0;
            fpwr = 0x02; /* Active mode */
            fstatus = 0x0000;
            fstdbywait = 0x01FF;
            factivewait = 0x01FF;
            fbankwait = 0x05;
            fotpwait = 0x08;
        }

        public long Size => 0x0E;

        private enum Registers : long
        {
            FOPT        = 0x00,
            FPWR        = 0x02,
            FSTATUS     = 0x04,
            FSTDBYWAIT  = 0x06,
            FACTIVEWAIT = 0x08,
            FBANKWAIT   = 0x0A,
            FOTPWAIT    = 0x0C,
        }

        private ushort fopt;
        private ushort fpwr;
        private ushort fstatus;
        private ushort fstdbywait;
        private ushort factivewait;
        private ushort fbankwait;
        private ushort fotpwait;
    }
}
