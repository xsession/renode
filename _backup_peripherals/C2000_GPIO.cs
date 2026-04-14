//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// TMS320C28x GPIO peripheral.
//
// The C28x GPIO module uses 32-bit port banks (A, B, C …).
// Each port bank has a set of 32-bit registers:
//
//   GPxDAT    — data (read pin values)
//   GPxSET    — write 1 to set output pin high
//   GPxCLEAR  — write 1 to drive output pin low
//   GPxTOGGLE — write 1 to toggle output pin
//   GPxDIR    — 0 = input, 1 = output
//   GPxPUD    — 0 = pull-up enabled, 1 = pull-up disabled
//   GPxMUX1   — peripheral mux select for pins 0-15
//   GPxMUX2   — peripheral mux select for pins 16-31
//
// The peripheral base address for port A is typically at
// offset 0x00 from the module base, port B at 0x20, port C at 0x40.
// Each port block is 0x20 (32 bytes = 8 × 32-bit registers).
//
using System;
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    public class C2000_GPIO : IDoubleWordPeripheral, IGPIOReceiver, IKnownSize, INumberedGPIOOutput
    {
        public C2000_GPIO(IMachine machine, int numberOfPorts = 3)
        {
            this.numberOfPorts = numberOfPorts;
            int totalPins = numberOfPorts * 32;

            dat    = new uint[numberOfPorts];
            dir    = new uint[numberOfPorts];
            pud    = new uint[numberOfPorts];
            mux1   = new uint[numberOfPorts];
            mux2   = new uint[numberOfPorts];
            output = new uint[numberOfPorts];

            Connections = new Dictionary<int, IGPIO>();
            for(int i = 0; i < totalPins; i++)
            {
                Connections[i] = new GPIO();
            }
        }

        // ------------------------------------------------------------------
        // IDoubleWordPeripheral
        // ------------------------------------------------------------------

        public uint ReadDoubleWord(long offset)
        {
            var (port, reg) = Decode(offset);
            if(port < 0 || port >= numberOfPorts)
            {
                this.Log(LogLevel.Warning,
                         "C2000_GPIO: Read from out-of-range offset 0x{0:X}", offset);
                return 0;
            }

            switch((PortReg)reg)
            {
            case PortReg.DAT:    return dat[port] | (output[port] & dir[port]);
            case PortReg.SET:    return output[port];
            case PortReg.CLEAR:  return 0;
            case PortReg.TOGGLE: return 0;
            case PortReg.DIR:    return dir[port];
            case PortReg.PUD:    return pud[port];
            case PortReg.MUX1:   return mux1[port];
            case PortReg.MUX2:   return mux2[port];
            default:
                this.Log(LogLevel.Warning,
                         "C2000_GPIO: Read from unknown port register 0x{0:X}", reg);
                return 0;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            var (port, reg) = Decode(offset);
            if(port < 0 || port >= numberOfPorts)
            {
                this.Log(LogLevel.Warning,
                         "C2000_GPIO: Write to out-of-range offset 0x{0:X}", offset);
                return;
            }

            switch((PortReg)reg)
            {
            case PortReg.DAT:
                /* Writing DAT directly sets the latch for output pins */
                output[port] = value & dir[port];
                DriveOutputs(port);
                break;
            case PortReg.SET:
                output[port] |= value & dir[port];
                DriveOutputs(port);
                break;
            case PortReg.CLEAR:
                output[port] &= ~(value & dir[port]);
                DriveOutputs(port);
                break;
            case PortReg.TOGGLE:
                output[port] ^= value & dir[port];
                DriveOutputs(port);
                break;
            case PortReg.DIR:
                dir[port] = value;
                DriveOutputs(port);
                break;
            case PortReg.PUD:
                pud[port] = value;
                break;
            case PortReg.MUX1:
                mux1[port] = value;
                break;
            case PortReg.MUX2:
                mux2[port] = value;
                break;
            default:
                this.Log(LogLevel.Warning,
                         "C2000_GPIO: Write 0x{0:X} to unknown port register 0x{1:X}",
                         value, reg);
                break;
            }
        }

        public void Reset()
        {
            for(int p = 0; p < numberOfPorts; p++)
            {
                dat[p] = dir[p] = pud[p] = mux1[p] = mux2[p] = output[p] = 0;
            }
        }

        // ------------------------------------------------------------------
        // IGPIOReceiver
        // ------------------------------------------------------------------

        public void OnGPIO(int number, bool value)
        {
            int port = number / 32;
            int bit  = number % 32;
            if(port < 0 || port >= numberOfPorts)
            {
                return;
            }

            if(value)
            {
                dat[port] |= (uint)(1 << bit);
            }
            else
            {
                dat[port] &= ~(uint)(1 << bit);
            }
        }

        // ------------------------------------------------------------------
        // IKnownSize
        // ------------------------------------------------------------------

        public long Size => (long)(numberOfPorts * 0x20);

        // ------------------------------------------------------------------
        // INumberedGPIOOutput
        // ------------------------------------------------------------------

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------

        private (int port, int reg) Decode(long offset)
        {
            int port = (int)(offset / 0x20);
            int reg  = (int)(offset % 0x20);
            return (port, reg);
        }

        private void DriveOutputs(int port)
        {
            for(int bit = 0; bit < 32; bit++)
            {
                bool isOutput = (dir[port] & (1u << bit)) != 0;
                if(isOutput)
                {
                    bool pinHigh = (output[port] & (1u << bit)) != 0;
                    int gpioIndex = port * 32 + bit;
                    Connections[gpioIndex].Set(pinHigh);
                }
            }
        }

        private enum PortReg
        {
            DAT    = 0x00,
            SET    = 0x04,
            CLEAR  = 0x08,
            TOGGLE = 0x0C,
            DIR    = 0x10,
            PUD    = 0x14,
            MUX1   = 0x18,
            MUX2   = 0x1C,
        }

        private readonly int numberOfPorts;
        private readonly uint[] dat;
        private readonly uint[] dir;
        private readonly uint[] pud;
        private readonly uint[] mux1;
        private readonly uint[] mux2;
        private readonly uint[] output;
    }
}
