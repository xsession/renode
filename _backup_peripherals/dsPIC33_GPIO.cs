//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// dsPIC33 GPIO port peripheral (PORTx / LATx / TRISx).
//
// Each I/O port exposes three 16-bit registers:
//   0x00  TRISx — direction (1=input, 0=output)
//   0x02  PORTx — read pin state / write output
//   0x04  LATx  — read/write output latch
//   0x06  ODCx  — open-drain control
//   0x08  CNPUx — change-notification pull-up
//   0x0A  CNPDx — change-notification pull-down
//   0x0C  CNCONx— change-notification control
//   0x0E  CNENx — change-notification enable
//   0x10  CNSTATx— change-notification status
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    public class dsPIC33_GPIO : IWordPeripheral, IGPIOReceiver, IKnownSize
    {
        public dsPIC33_GPIO(IMachine machine, int numberOfPins = 16)
        {
            if(numberOfPins < 1 || numberOfPins > 16)
                throw new ArgumentOutOfRangeException(nameof(numberOfPins));

            NumberOfPins = numberOfPins;
            Connections  = new GPIO[numberOfPins];
            for(int i = 0; i < numberOfPins; i++)
            {
                Connections[i] = new GPIO();
            }

            ChangeNotificationIRQ = new GPIO();
        }

        // ------------------------------------------------------------------
        // IWordPeripheral
        // ------------------------------------------------------------------

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TRIS:   return tris;
            case Registers.PORT:   return (ushort)(lat | pinInput);
            case Registers.LAT:    return lat;
            case Registers.ODC:    return odc;
            case Registers.CNPU:   return cnpu;
            case Registers.CNPD:   return cnpd;
            case Registers.CNCON:  return cncon;
            case Registers.CNEN:   return cnen;
            case Registers.CNSTAT:
                var s = cnstat;
                cnstat = 0;
                UpdateCNIRQ();
                return s;
            default:
                this.Log(LogLevel.Warning, "GPIO read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.TRIS:  tris = value; break;
            case Registers.PORT:  // writing to PORT is same as writing LAT
            case Registers.LAT:
                var changed = (ushort)(lat ^ value);
                lat = value;
                UpdateOutputs(changed);
                break;
            case Registers.ODC:   odc   = value; break;
            case Registers.CNPU:  cnpu  = value; break;
            case Registers.CNPD:  cnpd  = value; break;
            case Registers.CNCON: cncon = value; break;
            case Registers.CNEN:  cnen  = value; break;
            case Registers.CNSTAT:
                cnstat &= unchecked((ushort)~value);  // clear-by-write-1
                UpdateCNIRQ();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "GPIO write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        // ------------------------------------------------------------------
        // IGPIOReceiver — updated by pin stimuli from the outside world
        // ------------------------------------------------------------------

        public void OnGPIO(int number, bool value)
        {
            if((uint)number >= (uint)NumberOfPins)
            {
                this.Log(LogLevel.Warning, "GPIO pin {0} out of range", number);
                return;
            }

            ushort mask = (ushort)(1u << number);
            var oldInput = pinInput;

            if(value)
                pinInput |= mask;
            else
                pinInput &= unchecked((ushort)~mask);

            // Detect changes on CN-enabled pins
            if((cnen & mask) != 0 && (pinInput ^ oldInput & mask) != 0)
            {
                cnstat |= mask;
                UpdateCNIRQ();
            }
        }

        // ------------------------------------------------------------------
        // Reset
        // ------------------------------------------------------------------

        public void Reset()
        {
            tris    = 0xFFFF;  // all inputs on reset
            lat     = 0;
            odc     = 0;
            cnpu    = 0;
            cnpd    = 0;
            cncon   = 0;
            cnen    = 0;
            cnstat  = 0;
            pinInput = 0;

            foreach(var g in Connections) g.Unset();
            ChangeNotificationIRQ.Unset();
        }

        public long  Size       => 0x12;
        public int   NumberOfPins { get; }
        public GPIO  ChangeNotificationIRQ { get; }
        public GPIO[] Connections { get; }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private void UpdateOutputs(ushort changedBits)
        {
            for(int i = 0; i < NumberOfPins; i++)
            {
                ushort mask = (ushort)(1u << i);
                if((changedBits & mask) == 0) continue;
                // Drive output only if configured as output (TRIS bit = 0)
                if((tris & mask) == 0)
                {
                    Connections[i].Set((lat & mask) != 0);
                }
            }
        }

        private void UpdateCNIRQ()
        {
            ChangeNotificationIRQ.Set((cncon & CNCON_ON) != 0 && cnstat != 0);
        }

        private enum Registers : long
        {
            TRIS   = 0x00,
            PORT   = 0x02,
            LAT    = 0x04,
            ODC    = 0x06,
            CNPU   = 0x08,
            CNPD   = 0x0A,
            CNCON  = 0x0C,
            CNEN   = 0x0E,
            CNSTAT = 0x10,
        }

        private const ushort CNCON_ON = 1 << 15;

        private ushort tris;
        private ushort lat;
        private ushort odc;
        private ushort cnpu;
        private ushort cnpd;
        private ushort cncon;
        private ushort cnen;
        private ushort cnstat;
        private ushort pinInput;
    }
}
