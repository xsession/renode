//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
//
// 8051 (MCS-51) GPIO peripheral — Port P0–P3 model.
//
// Each port is 8 bits wide with quasi-bidirectional (P1-P3)
// or open-drain (P0) behaviour.
//
// Register map (byte-wide):
//   0x00  Px — Port latch / read pins
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    public class MCS51_GPIO : IBytePeripheral, IGPIOReceiver, IKnownSize, INumberedGPIOOutput
    {
        public MCS51_GPIO(IMachine machine)
        {
            Connections = new Dictionary<int, IGPIO>();
            for(int i = 0; i < 8; i++)
            {
                Connections[i] = new GPIO();
            }
            Reset();
        }

        public byte ReadByte(long offset)
        {
            switch(offset)
            {
            case 0x00: return pin;
            default:
                this.Log(LogLevel.Warning,
                         "MCS51_GPIO: Read from unknown offset 0x{0:X}", offset);
                return 0;
            }
        }

        public void WriteByte(long offset, byte value)
        {
            switch(offset)
            {
            case 0x00:
                latch = value;
                DriveOutputs();
                break;
            default:
                this.Log(LogLevel.Warning,
                         "MCS51_GPIO: Write 0x{0:X} to unknown offset 0x{1:X}", value, offset);
                break;
            }
        }

        public void Reset()
        {
            latch = 0xFF; /* Ports reset to 0xFF (all high / quasi-bidirectional) */
            pin = 0xFF;
            DriveOutputs();
        }

        public void OnGPIO(int number, bool value)
        {
            if(number < 0 || number > 7) return;
            if(value)
                pin |= (byte)(1 << number);
            else
                pin &= (byte)~(1 << number);
        }

        public long Size => 0x01;

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        private void DriveOutputs()
        {
            for(int i = 0; i < 8; i++)
            {
                Connections[i].Set((latch & (1 << i)) != 0);
            }
            /* Update pin register from latch (outputs read back their latch) */
            pin = latch;
        }

        private byte latch;
        private byte pin;
    }
}
