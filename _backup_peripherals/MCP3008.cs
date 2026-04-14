//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Utilities;


namespace Antmicro.Renode.Peripherals.Sensors
{
    // Microchip MCP3008 - 10-bit 8-channel SPI ADC
    public class MCP3008 : ISPIPeripheral
    {
        public MCP3008()
        {
            Reset();
        }

        public void FinishTransmission()
        {
            byteIndex = 0;
        }

        public void Reset()
        {
            byteIndex = 0;
        }

        public byte Transmit(byte data)
        {
            byte value = 0;
            switch(byteIndex)
            {
                case 0: startBit = (data & 0x01) != 0; break;
                case 1:
                    channelSelect = (byte)((data >> 4) & 0x07);
                    singleEnded = (data & 0x80) != 0;
                    int adcVal = channelSelect switch { 0=>Channel0, 1=>Channel1, 2=>Channel2, 3=>Channel3, 4=>Channel4, 5=>Channel5, 6=>Channel6, 7=>Channel7, _=>0 };
                    value = (byte)((adcVal >> 8) & 0x03);
                    lastLSB = (byte)(adcVal & 0xFF);
                    break;
                case 2: value = lastLSB; break;
            }
            byteIndex++;
            return value;
        }

        public int Channel0 { get; set; } = 512;
        public int Channel1 { get; set; } = 256;
        public int Channel2 { get; set; }
        public int Channel3 { get; set; }
        public int Channel4 { get; set; }
        public int Channel5 { get; set; }
        public int Channel6 { get; set; }
        public int Channel7 { get; set; }

        private bool startBit;
        private bool singleEnded;
        private byte channelSelect;
        private byte lastLSB;
        private int byteIndex;
    }
}
