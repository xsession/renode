//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// dsPIC30 GPIO peripheral (PORTx module).
//
// Register map (word-wide, stride per port, dsPIC30F Family Reference Manual, Section 11):
//   0x00  TRISx  — Data direction (1=input, 0=output)
//   0x02  PORTx  — Port read latch (input pins)
//   0x04  LATx   — Port output latch
//
using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    public class dsPIC30_GPIO : IWordPeripheral, IKnownSize
    {
        public dsPIC30_GPIO(IMachine machine, int numberOfPins = 16)
        {
            this.numberOfPins = numberOfPins;
            pinMask = (ushort)((1 << numberOfPins) - 1);
            Connections = new GPIO[numberOfPins];
            for(int i = 0; i < numberOfPins; i++)
                Connections[i] = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            switch((Registers)offset)
            {
            case Registers.TRISx: return tris;
            case Registers.PORTx:
                ushort pinState = 0;
                for(int i = 0; i < numberOfPins; i++)
                {
                    if((tris & (1 << i)) != 0) /* input */
                        pinState |= (ushort)((inputLatch >> i & 1) << i);
                    else /* output — read-back of latch */
                        pinState |= (ushort)((lat >> i & 1) << i);
                }
                return pinState;
            case Registers.LATx: return lat;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_GPIO: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteWord(long offset, ushort value)
        {
            switch((Registers)offset)
            {
            case Registers.TRISx:
                tris = (ushort)(value & pinMask);
                SyncOutputs();
                break;
            case Registers.PORTx:
                lat = (ushort)(value & pinMask);
                SyncOutputs();
                break;
            case Registers.LATx:
                lat = (ushort)(value & pinMask);
                SyncOutputs();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "dsPIC30_GPIO: Write 0x{0:X4} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            tris = pinMask; /* all inputs by default */
            lat  = 0;
            inputLatch = 0;
            for(int i = 0; i < numberOfPins; i++)
                Connections[i].Unset();
        }

        public void SetInput(int pin, bool state)
        {
            if(pin < 0 || pin >= numberOfPins) return;
            if(state) inputLatch |= (ushort)(1 << pin);
            else inputLatch &= (ushort)~(1 << pin);
        }

        public long Size => 0x06;
        public GPIO[] Connections { get; }

        private void SyncOutputs()
        {
            for(int i = 0; i < numberOfPins; i++)
            {
                if((tris & (1 << i)) == 0) /* output */
                {
                    if((lat & (1 << i)) != 0) Connections[i].Set();
                    else Connections[i].Unset();
                }
            }
        }

        private enum Registers : long
        {
            TRISx = 0x00,
            PORTx = 0x02,
            LATx  = 0x04,
        }

        private readonly int numberOfPins;
        private readonly ushort pinMask;
        private ushort tris, lat, inputLatch;
    }
}
