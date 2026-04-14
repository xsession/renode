//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// AVR GPIO port peripheral (PORTx/DDRx/PINx registers).
//
// Each AVR port exposes three byte-wide registers:
//   0x00  PINx  — read input pin values (write 1 to toggle output)
//   0x01  DDRx  — data direction (0=input, 1=output)
//   0x02  PORTx — output latch / pullup enable for inputs
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    public class AVR_GPIO : IBytePeripheral, IGPIOReceiver, IKnownSize, INumberedGPIOOutput
    {
        public AVR_GPIO(IMachine machine)
        {
            Connections = new Dictionary<int, IGPIO>();
            for(int i = 0; i < 8; i++)
            {
                Connections[i] = new GPIO();
            }
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
            case 0x00: return pin;
            case 0x01: return ddr;
            case 0x02: return port;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_GPIO: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
            case 0x00:
                /* Writing to PIN toggles the output latch bits */
                port ^= value;
                DriveOutputs();
                break;
            case 0x01:
                ddr = value;
                DriveOutputs();
                break;
            case 0x02:
                port = value;
                DriveOutputs();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "AVR_GPIO: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            pin = 0;
            ddr = 0;
            port = 0;
        }

        public void OnGPIO(int number, bool value)
        {
            if(number < 0 || number > 7) return;
            if(value)
                pin |= (byte)(1 << number);
            else
                pin &= (byte)~(1 << number);
        }

        public long Size => 0x03;

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        private void DriveOutputs()
        {
            for(int i = 0; i < 8; i++)
            {
                if((ddr & (1 << i)) != 0)
                {
                    Connections[i].Set((port & (1 << i)) != 0);
                }
            }
        }

        private byte pin;
        private byte ddr;
        private byte port;
    }
}
