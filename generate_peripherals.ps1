$videoDir   = "c:\GIT\renode\src\Infrastructure\src\Emulator\Peripherals\Peripherals\Video"
$miscDir    = "c:\GIT\renode\src\Infrastructure\src\Emulator\Peripherals\Peripherals\Miscellaneous"
$sensorsDir = "c:\GIT\renode\src\Infrastructure\src\Emulator\Peripherals\Peripherals\Sensors"

$license = @"
//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
"@

Write-Host "Generating servo/motor drivers, displays, and other peripherals..."

# ══════════════════════════════════════════════════════════════
# SERVO / MOTOR DRIVERS (Miscellaneous/)
# ══════════════════════════════════════════════════════════════

# ── PCA9685 - 16-channel I2C PWM/Servo driver ──
$f = @"
$license
using System;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // NXP PCA9685 - 16-channel 12-bit PWM I2C driver (servos, LEDs)
    public class PCA9685 : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>
    {
        public PCA9685()
        {
            RegistersCollection = new ByteRegisterCollection(this);
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
            Array.Clear(channelOnL, 0, 16);
            Array.Clear(channelOnH, 0, 16);
            Array.Clear(channelOffL, 0, 16);
            Array.Clear(channelOffH, 0, 16);
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0) return;
            registerAddress = data[0];
            for(var i = 1; i < data.Length; i++)
            {
                RegistersCollection.Write(registerAddress, data[i]);
                registerAddress++;
            }
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                result[i] = RegistersCollection.Read(registerAddress);
                registerAddress++;
            }
            return result;
        }

        public void FinishTransmission() { }

        // Expose duty cycle for each channel (0-4095)
        public int GetChannelDuty(int channel)
        {
            if(channel < 0 || channel > 15) return 0;
            return (channelOffL[channel] | (channelOffH[channel] << 8)) & 0xFFF;
        }

        public ByteRegisterCollection RegistersCollection { get; }

        private void DefineRegisters()
        {
            // MODE1 @ 0x00, MODE2 @ 0x01
            RegistersCollection.DefineRegister(0x00, 0x11); // MODE1: sleep on, allcall
            RegistersCollection.DefineRegister(0x01, 0x04); // MODE2: totem pole
            // Channels 0-15: each has ON_L, ON_H, OFF_L, OFF_H at 0x06 + 4*ch
            for(var ch = 0; ch < 16; ch++)
            {
                var c = ch;
                var baseAddr = 0x06 + 4 * ch;
                RegistersCollection.DefineRegister(baseAddr, 0)
                    .WithValueField(0, 8, name: $"LED{c}_ON_L",
                        writeCallback: (_, val) => channelOnL[c] = (byte)val,
                        valueProviderCallback: _ => channelOnL[c]);
                RegistersCollection.DefineRegister(baseAddr + 1, 0)
                    .WithValueField(0, 8, name: $"LED{c}_ON_H",
                        writeCallback: (_, val) => channelOnH[c] = (byte)(val & 0x1F),
                        valueProviderCallback: _ => channelOnH[c]);
                RegistersCollection.DefineRegister(baseAddr + 2, 0)
                    .WithValueField(0, 8, name: $"LED{c}_OFF_L",
                        writeCallback: (_, val) => channelOffL[c] = (byte)val,
                        valueProviderCallback: _ => channelOffL[c]);
                RegistersCollection.DefineRegister(baseAddr + 3, 0)
                    .WithValueField(0, 8, name: $"LED{c}_OFF_H",
                        writeCallback: (_, val) => channelOffH[c] = (byte)(val & 0x1F),
                        valueProviderCallback: _ => channelOffH[c]);
            }
            // ALL_LED ON/OFF at 0xFA-0xFD
            RegistersCollection.DefineRegister(0xFA);
            RegistersCollection.DefineRegister(0xFB);
            RegistersCollection.DefineRegister(0xFC);
            RegistersCollection.DefineRegister(0xFD);
            // PRE_SCALE at 0xFE (default 30 = ~200Hz)
            RegistersCollection.DefineRegister(0xFE, 0x1E);
        }

        private byte[] channelOnL = new byte[16];
        private byte[] channelOnH = new byte[16];
        private byte[] channelOffL = new byte[16];
        private byte[] channelOffH = new byte[16];
        private int registerAddress;
    }
}
"@
Set-Content "$miscDir\PCA9685.cs" $f -Encoding UTF8

# ── DRV8825 - stepper motor driver (GPIO-based stub) ──
$f = @"
$license
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.GPIOPort;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // TI DRV8825 - stepper motor driver (GPIO interface stub)
    // Pins: STEP (GPIO input), DIR (GPIO input), ENABLE, FAULT (output)
    public class DRV8825 : IGPIOReceiver, IPeripheral
    {
        public DRV8825()
        {
            Fault = new GPIO();
            Reset();
        }

        public void Reset()
        {
            StepCount = 0;
            Direction = false;
            Fault.Set(true); // active-low, no fault
        }

        public void OnGPIO(int number, bool value)
        {
            switch(number)
            {
                case 0: // STEP pin - rising edge
                    if(value)
                    {
                        StepCount += Direction ? -1 : 1;
                        this.Log(LogLevel.Debug, "Step {0}, total: {1}", Direction ? "CCW" : "CW", StepCount);
                    }
                    break;
                case 1: // DIR pin
                    Direction = value;
                    break;
                case 2: // ENABLE pin (active low)
                    Enabled = !value;
                    break;
            }
        }

        public int StepCount { get; set; }
        public bool Direction { get; set; }
        public bool Enabled { get; set; } = true;
        public GPIO Fault { get; }
    }
}
"@
Set-Content "$miscDir\DRV8825.cs" $f -Encoding UTF8

# ── A4988 - stepper motor driver ──
$f = @"
$license
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.GPIOPort;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Allegro A4988 - stepper motor driver (GPIO interface stub)
    public class A4988 : IGPIOReceiver, IPeripheral
    {
        public A4988()
        {
            Reset();
        }

        public void Reset()
        {
            StepCount = 0;
            Direction = false;
        }

        public void OnGPIO(int number, bool value)
        {
            switch(number)
            {
                case 0: // STEP - rising edge
                    if(value)
                    {
                        StepCount += Direction ? -1 : 1;
                        this.Log(LogLevel.Debug, "Step {0}, total: {1}", Direction ? "CCW" : "CW", StepCount);
                    }
                    break;
                case 1: // DIR
                    Direction = value;
                    break;
                case 2: // ENABLE (active low)
                    Enabled = !value;
                    break;
            }
        }

        public int StepCount { get; set; }
        public bool Direction { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
"@
Set-Content "$miscDir\A4988.cs" $f -Encoding UTF8

# ── TB6612FNG - dual DC motor driver ──
$f = @"
$license
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.GPIOPort;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Toshiba TB6612FNG - dual DC motor driver (GPIO stub)
    // AIN1/AIN2/BIN1/BIN2 control direction, STBY enables
    public class TB6612FNG : IGPIOReceiver, IPeripheral
    {
        public TB6612FNG()
        {
            Reset();
        }

        public void Reset()
        {
            motorAForward = false;
            motorAReverse = false;
            motorBForward = false;
            motorBReverse = false;
        }

        public void OnGPIO(int number, bool value)
        {
            switch(number)
            {
                case 0: motorAForward = value; break; // AIN1
                case 1: motorAReverse = value; break; // AIN2
                case 2: motorBForward = value; break; // BIN1
                case 3: motorBReverse = value; break; // BIN2
                case 4: Standby = !value; break;      // STBY (active low = standby)
            }
            this.Log(LogLevel.Debug, "MotorA: {0}, MotorB: {1}, Standby: {2}",
                motorAForward && !motorAReverse ? "FWD" : (!motorAForward && motorAReverse ? "REV" : "STOP"),
                motorBForward && !motorBReverse ? "FWD" : (!motorBForward && motorBReverse ? "REV" : "STOP"),
                Standby);
        }

        public bool Standby { get; set; }
        private bool motorAForward, motorAReverse, motorBForward, motorBReverse;
    }
}
"@
Set-Content "$miscDir\TB6612FNG.cs" $f -Encoding UTF8

# ── TMC2209 - UART stepper driver (simplified) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.UART;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Trinamic TMC2209 - UART-based stepper motor driver
    // Responds to UART register read/write datagrams
    public class TMC2209 : IPeripheral, IUART
    {
        public TMC2209()
        {
            Reset();
        }

        public void Reset()
        {
            gconf = 0x00000141;
            chopconf = 0x10000053;
            drvStatus = 0x00000000;
            ihold_irun = 0x00001F00;
        }

        public void WriteChar(byte value)
        {
            rxBuffer[rxIndex++] = value;
            if(rxIndex >= 8) // full datagram received
            {
                ProcessDatagram();
                rxIndex = 0;
            }
        }

        public event Action<byte> CharReceived;

        public uint BaudRate { get; set; } = 115200;
        public Bits StopBits => Bits.One;
        public Bits ParityBit => Bits.None;

        private void ProcessDatagram()
        {
            var sync = rxBuffer[0];
            var addr = rxBuffer[1];
            var reg = rxBuffer[2] & 0x7F;
            var isWrite = (rxBuffer[2] & 0x80) != 0;

            if(isWrite)
            {
                var val = (uint)((rxBuffer[3] << 24) | (rxBuffer[4] << 16) | (rxBuffer[5] << 8) | rxBuffer[6]);
                this.Log(LogLevel.Debug, "TMC2209 write reg 0x{0:X2} = 0x{1:X8}", reg, val);
                switch(reg)
                {
                    case 0x00: gconf = val; break;
                    case 0x10: ihold_irun = val; break;
                    case 0x6C: chopconf = val; break;
                }
            }
            else
            {
                // Send read response: sync(0x05) + master_addr(0xFF) + reg + data(4) + crc
                uint val = 0;
                switch(reg)
                {
                    case 0x00: val = gconf; break;
                    case 0x10: val = ihold_irun; break;
                    case 0x6C: val = chopconf; break;
                    case 0x6F: val = drvStatus; break;
                    default: val = 0; break;
                }
                var resp = new byte[] { 0x05, 0xFF, (byte)reg,
                    (byte)((val >> 24) & 0xFF), (byte)((val >> 16) & 0xFF),
                    (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF), 0x00 };
                // Simple CRC
                byte crc = 0;
                for(int i = 0; i < 7; i++) crc = (byte)(crc + resp[i]);
                resp[7] = crc;
                foreach(var b in resp)
                    CharReceived?.Invoke(b);
            }
        }

        private uint gconf, chopconf, drvStatus, ihold_irun;
        private byte[] rxBuffer = new byte[8];
        private int rxIndex;
    }
}
"@
Set-Content "$miscDir\TMC2209.cs" $f -Encoding UTF8

Write-Host "Servo/motor drivers done (5 files)."

# ══════════════════════════════════════════════════════════════
# DISPLAY MODELS (Video/)
# ══════════════════════════════════════════════════════════════

# ── SSD1306 - 128x64 OLED (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Video
{
    // Solomon Systech SSD1306 - 128x64 monochrome OLED controller (I2C)
    public class SSD1306 : II2CPeripheral, IPeripheral
    {
        public SSD1306()
        {
            buffer = new byte[128 * 64 / 8]; // 1024 bytes GDDRAM
            Reset();
        }

        public void Reset()
        {
            displayOn = false;
            contrast = 0x7F;
            pageAddress = 0;
            columnAddress = 0;
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void Write(byte[] data)
        {
            if(data.Length < 2) return;
            var controlByte = data[0];
            var isContinuation = (controlByte & 0x80) != 0;
            var isData = (controlByte & 0x40) != 0;

            for(var i = 1; i < data.Length; i++)
            {
                if(isData)
                {
                    // Write to GDDRAM
                    var addr = pageAddress * 128 + columnAddress;
                    if(addr < buffer.Length)
                        buffer[addr] = data[i];
                    columnAddress = (columnAddress + 1) & 0x7F;
                }
                else
                {
                    ProcessCommand(data[i]);
                }
            }
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                var addr = pageAddress * 128 + columnAddress;
                result[i] = addr < buffer.Length ? buffer[addr] : (byte)0;
                columnAddress = (columnAddress + 1) & 0x7F;
            }
            return result;
        }

        public void FinishTransmission() { }

        private void ProcessCommand(byte cmd)
        {
            if(cmd <= 0x0F) { columnAddress = (columnAddress & 0xF0) | (cmd & 0x0F); } // lower column
            else if(cmd >= 0x10 && cmd <= 0x1F) { columnAddress = (columnAddress & 0x0F) | ((cmd & 0x0F) << 4); } // upper column
            else if(cmd >= 0xB0 && cmd <= 0xB7) { pageAddress = cmd & 0x07; }
            else if(cmd == 0xAE) { displayOn = false; this.Log(LogLevel.Debug, "Display OFF"); }
            else if(cmd == 0xAF) { displayOn = true; this.Log(LogLevel.Debug, "Display ON"); }
            else if(cmd == 0x81) { nextIsContrast = true; }
            else if(nextIsContrast) { contrast = cmd; nextIsContrast = false; }
        }

        public bool DisplayOn => displayOn;
        public byte Contrast => contrast;

        private byte[] buffer;
        private bool displayOn;
        private byte contrast;
        private int pageAddress;
        private int columnAddress;
        private bool nextIsContrast;
    }
}
"@
Set-Content "$videoDir\SSD1306.cs" $f -Encoding UTF8

# ── SH1106 - 132x64 OLED (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Video
{
    // Sino Wealth SH1106 - 132x64 monochrome OLED controller (I2C)
    public class SH1106 : II2CPeripheral, IPeripheral
    {
        public SH1106()
        {
            buffer = new byte[132 * 64 / 8]; // 1056 bytes
            Reset();
        }

        public void Reset()
        {
            displayOn = false;
            pageAddress = 0;
            columnAddress = 0;
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void Write(byte[] data)
        {
            if(data.Length < 2) return;
            var isData = (data[0] & 0x40) != 0;
            for(var i = 1; i < data.Length; i++)
            {
                if(isData)
                {
                    var addr = pageAddress * 132 + columnAddress;
                    if(addr < buffer.Length) buffer[addr] = data[i];
                    columnAddress = Math.Min(columnAddress + 1, 131);
                }
                else
                {
                    ProcessCommand(data[i]);
                }
            }
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                var addr = pageAddress * 132 + columnAddress;
                result[i] = addr < buffer.Length ? buffer[addr] : (byte)0;
                columnAddress = Math.Min(columnAddress + 1, 131);
            }
            return result;
        }

        public void FinishTransmission() { }

        private void ProcessCommand(byte cmd)
        {
            if(cmd <= 0x0F) columnAddress = (columnAddress & 0xF0) | (cmd & 0x0F);
            else if(cmd >= 0x10 && cmd <= 0x1F) columnAddress = (columnAddress & 0x0F) | ((cmd & 0x0F) << 4);
            else if(cmd >= 0xB0 && cmd <= 0xB7) pageAddress = cmd & 0x07;
            else if(cmd == 0xAE) displayOn = false;
            else if(cmd == 0xAF) displayOn = true;
        }

        public bool DisplayOn => displayOn;
        private byte[] buffer;
        private bool displayOn;
        private int pageAddress, columnAddress;
    }
}
"@
Set-Content "$videoDir\SH1106.cs" $f -Encoding UTF8

# ── SSD1327 - 128x128 grayscale OLED (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Video
{
    // Solomon Systech SSD1327 - 128x128 16-level grayscale OLED (I2C)
    public class SSD1327 : II2CPeripheral, IPeripheral
    {
        public SSD1327()
        {
            buffer = new byte[128 * 128 / 2]; // 4-bit per pixel
            Reset();
        }

        public void Reset()
        {
            displayOn = false;
            columnAddress = 0;
            rowAddress = 0;
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void Write(byte[] data)
        {
            if(data.Length < 2) return;
            var isData = (data[0] & 0x40) != 0;
            for(var i = 1; i < data.Length; i++)
            {
                if(isData)
                {
                    var addr = rowAddress * 64 + columnAddress;
                    if(addr < buffer.Length) buffer[addr] = data[i];
                    columnAddress++;
                    if(columnAddress >= 64) { columnAddress = 0; rowAddress++; }
                }
                else
                {
                    if(data[i] == 0xAE) displayOn = false;
                    else if(data[i] == 0xAF) displayOn = true;
                }
            }
        }

        public byte[] Read(int count) => new byte[count];
        public void FinishTransmission() { }

        public bool DisplayOn => displayOn;
        private byte[] buffer;
        private bool displayOn;
        private int columnAddress, rowAddress;
    }
}
"@
Set-Content "$videoDir\SSD1327.cs" $f -Encoding UTF8

# ── Helper function for SPI TFT displays ──
function New-SPIDisplay {
    param(
        [string]$ClassName,
        [string]$Comment,
        [int]$Width,
        [int]$Height,
        [int]$Bpp,    # bytes per pixel (2 = RGB565, 3 = RGB888)
        [string]$ExtraFields = ""
    )

    return @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Video
{
    // $Comment
    public class ${ClassName} : ISPIPeripheral, IPeripheral
    {
        public ${ClassName}()
        {
            buffer = new byte[${Width} * ${Height} * ${Bpp}];
            Reset();
        }

        public void Reset()
        {
            dataMode = false;
            currentCommand = 0;
            pixelIndex = 0;
            displayOn = false;
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void FinishTransmission()
        {
            // CS deasserted
        }

        public byte Transmit(byte data)
        {
            if(!dataMode)
            {
                // Command byte
                currentCommand = data;
                switch(data)
                {
                    case 0x01: Reset(); break; // Software reset
                    case 0x11: sleepMode = false; break; // Sleep out
                    case 0x10: sleepMode = true; break;  // Sleep in
                    case 0x29: displayOn = true; this.Log(LogLevel.Debug, "Display ON"); break;
                    case 0x28: displayOn = false; break;
                    case 0x2C: pixelIndex = 0; dataMode = true; break; // Memory write
                    case 0x36: dataMode = true; break; // MADCTL
                    case 0x3A: dataMode = true; break; // COLMOD (pixel format)
                }
            }
            else
            {
                if(currentCommand == 0x2C)
                {
                    if(pixelIndex < buffer.Length)
                        buffer[pixelIndex++] = data;
                }
                else
                {
                    // Parameter byte for other commands
                    dataMode = false;
                }
            }
            return 0;
        }

        public bool DisplayOn => displayOn;
        public int Width => ${Width};
        public int Height => ${Height};

        private byte[] buffer;
        private bool dataMode;
        private byte currentCommand;
        private int pixelIndex;
        private bool displayOn;
        private bool sleepMode = true;
$ExtraFields
    }
}
"@
}

# ── ST7735 - 128x160 TFT (SPI) ──
$f = New-SPIDisplay -ClassName "ST7735" -Comment "Sitronix ST7735 - 128x160 RGB565 TFT LCD controller (SPI)" -Width 128 -Height 160 -Bpp 2
Set-Content "$videoDir\ST7735.cs" $f -Encoding UTF8

# ── ST7789 - 240x320 TFT (SPI) ──
$f = New-SPIDisplay -ClassName "ST7789" -Comment "Sitronix ST7789 - 240x320 RGB565 TFT LCD controller (SPI)" -Width 240 -Height 320 -Bpp 2
Set-Content "$videoDir\ST7789.cs" $f -Encoding UTF8

# ── ILI9341 - 240x320 TFT (SPI) ──
$f = New-SPIDisplay -ClassName "ILI9341" -Comment "Ilitek ILI9341 - 240x320 RGB565 TFT LCD controller (SPI)" -Width 240 -Height 320 -Bpp 2
Set-Content "$videoDir\ILI9341.cs" $f -Encoding UTF8

# ── ILI9488 - 320x480 TFT (SPI) ──
$f = New-SPIDisplay -ClassName "ILI9488" -Comment "Ilitek ILI9488 - 320x480 RGB666 TFT LCD controller (SPI)" -Width 320 -Height 480 -Bpp 3
Set-Content "$videoDir\ILI9488.cs" $f -Encoding UTF8

# ── GC9A01 - 240x240 round TFT (SPI) ──
$f = New-SPIDisplay -ClassName "GC9A01" -Comment "GalaxyCore GC9A01 - 240x240 round RGB565 TFT LCD controller (SPI)" -Width 240 -Height 240 -Bpp 2
Set-Content "$videoDir\GC9A01.cs" $f -Encoding UTF8

# ── PCD8544 / Nokia 5110 - 84x48 LCD (SPI) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Video
{
    // Philips PCD8544 - 84x48 monochrome LCD controller (Nokia 5110) (SPI)
    public class PCD8544 : ISPIPeripheral, IPeripheral
    {
        public PCD8544()
        {
            buffer = new byte[84 * 48 / 8]; // 504 bytes
            Reset();
        }

        public void Reset()
        {
            xAddress = 0;
            yAddress = 0;
            displayOn = false;
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void FinishTransmission() { }

        public byte Transmit(byte data)
        {
            if(dcPin) // Data mode
            {
                var addr = yAddress * 84 + xAddress;
                if(addr < buffer.Length) buffer[addr] = data;
                xAddress++;
                if(xAddress >= 84) { xAddress = 0; yAddress = (yAddress + 1) % 6; }
            }
            else // Command mode
            {
                if((data & 0x80) != 0) { xAddress = data & 0x7F; } // Set X
                else if((data & 0x40) != 0) { yAddress = data & 0x07; } // Set Y
                else if(data == 0x20) { /* Function set: basic */ }
                else if(data == 0x21) { /* Function set: extended */ }
                else if(data == 0x0C) { displayOn = true; }
                else if(data == 0x08) { displayOn = false; }
            }
            return 0;
        }

        // DC pin state must be set by the platform using GPIO
        public bool DCPin { set { dcPin = value; } }

        public bool DisplayOn => displayOn;
        private byte[] buffer;
        private bool displayOn;
        private bool dcPin;
        private int xAddress, yAddress;
    }
}
"@
Set-Content "$videoDir\PCD8544.cs" $f -Encoding UTF8

# ── UC1701 - 102x64 LCD (SPI) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Video
{
    // UltraChip UC1701 - 102x64 monochrome LCD controller (SPI)
    public class UC1701 : ISPIPeripheral, IPeripheral
    {
        public UC1701()
        {
            buffer = new byte[102 * 64 / 8];
            Reset();
        }

        public void Reset()
        {
            pageAddress = 0;
            columnAddress = 0;
            displayOn = false;
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void FinishTransmission() { }

        public byte Transmit(byte data)
        {
            if(dcPin)
            {
                var addr = pageAddress * 102 + columnAddress;
                if(addr < buffer.Length) buffer[addr] = data;
                columnAddress = Math.Min(columnAddress + 1, 101);
            }
            else
            {
                if(data <= 0x0F) columnAddress = (columnAddress & 0xF0) | (data & 0x0F);
                else if(data >= 0x10 && data <= 0x1F) columnAddress = (columnAddress & 0x0F) | ((data & 0x0F) << 4);
                else if(data >= 0xB0 && data <= 0xBF) pageAddress = data & 0x0F;
                else if(data == 0xAE) displayOn = false;
                else if(data == 0xAF) displayOn = true;
                else if(data == 0xE2) Reset();
            }
            return 0;
        }

        public bool DCPin { set { dcPin = value; } }
        public bool DisplayOn => displayOn;
        private byte[] buffer;
        private bool displayOn;
        private bool dcPin;
        private int pageAddress, columnAddress;
    }
}
"@
Set-Content "$videoDir\UC1701.cs" $f -Encoding UTF8

# ── HD44780 via PCF8574 - character LCD (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Video
{
    // Hitachi HD44780 character LCD via PCF8574 I2C backpack
    // Typical 16x2 or 20x4 LCD. Data sent as nibbles via I2C expander.
    public class HD44780_PCF8574 : II2CPeripheral, IPeripheral
    {
        public HD44780_PCF8574(int columns = 16, int rows = 2)
        {
            this.columns = columns;
            this.rows = rows;
            ddram = new byte[columns * rows];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(ddram, 0, ddram.Length);
            cursorPos = 0;
            displayOn = false;
            lastNibble = 0;
            highNibbleNext = true;
        }

        public void Write(byte[] data)
        {
            foreach(var b in data)
            {
                // PCF8574 bit layout: D7 D6 D5 D4 BL EN RW RS
                var rs = (b & 0x01) != 0;    // Register Select
                var en = (b & 0x04) != 0;    // Enable
                var nibble = (b >> 4) & 0x0F;

                if(en) // Latch on EN high
                {
                    if(highNibbleNext)
                    {
                        lastNibble = (byte)(nibble << 4);
                        highNibbleNext = false;
                    }
                    else
                    {
                        var fullByte = (byte)(lastNibble | nibble);
                        highNibbleNext = true;

                        if(rs) // Data
                        {
                            if(cursorPos < ddram.Length)
                                ddram[cursorPos++] = fullByte;
                        }
                        else // Command
                        {
                            ProcessCommand(fullByte);
                        }
                    }
                }
            }
        }

        public byte[] Read(int count) => new byte[count];
        public void FinishTransmission() { }

        private void ProcessCommand(byte cmd)
        {
            if(cmd == 0x01) { Array.Clear(ddram, 0, ddram.Length); cursorPos = 0; } // Clear
            else if(cmd == 0x02) { cursorPos = 0; } // Home
            else if((cmd & 0x80) != 0) { cursorPos = cmd & 0x7F; } // Set DDRAM address
            else if((cmd & 0x08) != 0) { displayOn = (cmd & 0x04) != 0; } // Display on/off
        }

        public string GetText()
        {
            var text = "";
            for(var r = 0; r < rows; r++)
            {
                for(var c = 0; c < columns; c++)
                {
                    var ch = ddram[r * columns + c];
                    text += ch >= 0x20 && ch < 0x7F ? (char)ch : ' ';
                }
                if(r < rows - 1) text += "\n";
            }
            return text;
        }

        public bool DisplayOn => displayOn;
        private readonly int columns, rows;
        private byte[] ddram;
        private int cursorPos;
        private bool displayOn;
        private byte lastNibble;
        private bool highNibbleNext;
    }
}
"@
Set-Content "$videoDir\HD44780_PCF8574.cs" $f -Encoding UTF8

# ── MAX7219 - 8x8 LED matrix / 7-segment (SPI) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Video
{
    // Maxim MAX7219 - 8-digit LED / 8x8 LED matrix driver (SPI)
    public class MAX7219 : ISPIPeripheral, IPeripheral
    {
        public MAX7219()
        {
            digits = new byte[8];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(digits, 0, 8);
            highByte = 0;
            receivingHigh = true;
            shutdown = true;
            intensity = 0;
            scanLimit = 7;
        }

        public void FinishTransmission()
        {
            receivingHigh = true;
        }

        public byte Transmit(byte data)
        {
            if(receivingHigh)
            {
                highByte = data;
                receivingHigh = false;
            }
            else
            {
                receivingHigh = true;
                var reg = highByte & 0x0F;
                switch(highByte)
                {
                    case 0x09: decodeMode = data; break;
                    case 0x0A: intensity = data; break;
                    case 0x0B: scanLimit = data & 0x07; break;
                    case 0x0C: shutdown = data == 0; break;
                    case 0x0F: displayTest = data != 0; break;
                    default:
                        if(highByte >= 0x01 && highByte <= 0x08)
                            digits[highByte - 1] = data;
                        break;
                }
            }
            return 0;
        }

        public byte[] Digits => digits;
        public bool Shutdown => shutdown;
        public byte Intensity => intensity;

        private byte[] digits;
        private byte highByte;
        private bool receivingHigh;
        private bool shutdown;
        private byte intensity;
        private int scanLimit;
        private byte decodeMode;
        private bool displayTest;
    }
}
"@
Set-Content "$videoDir\MAX7219.cs" $f -Encoding UTF8

# ── HT16K33 - LED matrix / 7-segment (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Video
{
    // Holtek HT16K33 - 16x8 LED matrix / key scan driver (I2C)
    public class HT16K33 : II2CPeripheral, IPeripheral
    {
        public HT16K33()
        {
            displayRam = new byte[16]; // 16 bytes of display RAM
            Reset();
        }

        public void Reset()
        {
            Array.Clear(displayRam, 0, 16);
            systemOn = false;
            displayOn = false;
            brightness = 15;
            blinkRate = 0;
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0) return;
            var cmd = data[0];

            if(cmd <= 0x0F && data.Length >= 2) // Display RAM write
            {
                for(var i = 1; i < data.Length && (cmd + i - 1) < 16; i++)
                    displayRam[cmd + i - 1] = data[i];
            }
            else if((cmd & 0xF0) == 0x20) { systemOn = (cmd & 0x01) != 0; } // System setup
            else if((cmd & 0xF0) == 0x80) // Display setup
            {
                displayOn = (cmd & 0x01) != 0;
                blinkRate = (cmd >> 1) & 0x03;
            }
            else if((cmd & 0xF0) == 0xE0) { brightness = cmd & 0x0F; } // Dimming
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            Array.Copy(displayRam, 0, result, 0, Math.Min(count, 16));
            return result;
        }

        public void FinishTransmission() { }

        public byte[] DisplayRam => displayRam;
        public bool DisplayOn => displayOn;
        public int Brightness => brightness;

        private byte[] displayRam;
        private bool systemOn, displayOn;
        private int brightness, blinkRate;
    }
}
"@
Set-Content "$videoDir\HT16K33.cs" $f -Encoding UTF8

Write-Host "Display models done (12 files)."

# ══════════════════════════════════════════════════════════════
# RFID / NFC
# ══════════════════════════════════════════════════════════════

# ── PN532 - NFC/RFID (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // NXP PN532 - NFC/RFID reader/writer (I2C)
    public class PN532 : II2CPeripheral, IPeripheral
    {
        public PN532()
        {
            Reset();
        }

        public void Reset()
        {
            readBuffer = new byte[0];
            readIndex = 0;
        }

        public void Write(byte[] data)
        {
            if(data.Length < 6) return;
            // Expected: 0x00 0x00 0xFF LEN LCS [TFI CMD ...] DCS 0x00
            var len = data[3];
            if(data.Length < 6 + len) return;
            var tfi = data[5]; // 0xD4 = host-to-PN532
            var cmd = data[6];

            this.Log(LogLevel.Debug, "PN532 command: 0x{0:X2}", cmd);

            switch(cmd)
            {
                case 0x02: // GetFirmwareVersion
                    readBuffer = BuildResponse(0x03, new byte[] { 0x07, 0x03, 0x07, 0x00 }); // IC=PN532, FW 1.6, support ISO14443A/B
                    break;
                case 0x14: // SAMConfiguration
                    readBuffer = BuildResponse(0x15, new byte[0]);
                    break;
                case 0x4A: // InListPassiveTarget
                    if(TagUID != null && TagUID.Length > 0)
                    {
                        // Return 1 target found
                        var resp = new byte[5 + TagUID.Length];
                        resp[0] = 0x01; // NbTg = 1
                        resp[1] = 0x01; // Tg = 1
                        resp[2] = 0x04; // SENS_RES MSB
                        resp[3] = 0x00; // SENS_RES LSB
                        resp[4] = (byte)TagUID.Length;
                        Array.Copy(TagUID, 0, resp, 5, TagUID.Length);
                        readBuffer = BuildResponse(0x4B, resp);
                    }
                    else
                    {
                        readBuffer = BuildResponse(0x4B, new byte[] { 0x00 }); // No targets
                    }
                    break;
                default:
                    readBuffer = BuildResponse((byte)(cmd + 1), new byte[0]);
                    break;
            }
            readIndex = 0;
        }

        public byte[] Read(int count)
        {
            // First byte is status (0x01 = ready)
            var result = new byte[count];
            result[0] = 0x01; // Ready
            for(var i = 1; i < count && readIndex < readBuffer.Length; i++)
                result[i] = readBuffer[readIndex++];
            return result;
        }

        public void FinishTransmission() { }

        // Set this to simulate a tag present
        public byte[] TagUID { get; set; } = new byte[] { 0x04, 0x11, 0x22, 0x33 };

        private byte[] BuildResponse(byte cmd, byte[] payload)
        {
            var len = (byte)(payload.Length + 1); // +1 for TFI
            var lcs = (byte)(~len + 1);
            byte tfi = 0xD5;
            var dcs = tfi;
            dcs = (byte)(dcs + cmd);
            foreach(var b in payload) dcs = (byte)(dcs + b);
            dcs = (byte)(~dcs + 1);

            var resp = new byte[7 + payload.Length];
            resp[0] = 0x00; resp[1] = 0x00; resp[2] = 0xFF; // preamble + start
            resp[3] = len; resp[4] = lcs;
            resp[5] = tfi; resp[6] = cmd;
            Array.Copy(payload, 0, resp, 7, payload.Length);
            // append DCS and postamble (simplified)
            return resp;
        }

        private byte[] readBuffer;
        private int readIndex;
    }
}
"@
Set-Content "$miscDir\PN532.cs" $f -Encoding UTF8

# ── MFRC522 - RFID (SPI) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // NXP MFRC522 - contactless RFID reader (SPI)
    public class MFRC522 : ISPIPeripheral, IPeripheral
    {
        public MFRC522()
        {
            Reset();
        }

        public void Reset()
        {
            registerAddress = 0;
            fifoBuffer = new byte[64];
            fifoIndex = 0;
            fifoLevel = 0;
        }

        public void FinishTransmission()
        {
            byteCount = 0;
        }

        public byte Transmit(byte data)
        {
            if(byteCount == 0)
            {
                registerAddress = (byte)((data >> 1) & 0x3F);
                isRead = (data & 0x80) != 0;
                byteCount++;
                if(isRead)
                {
                    return ReadRegister(registerAddress);
                }
                return 0;
            }
            else
            {
                byteCount++;
                if(isRead)
                {
                    // Continued read - address from first byte
                    return ReadRegister(registerAddress);
                }
                else
                {
                    WriteRegister(registerAddress, data);
                    return 0;
                }
            }
        }

        private byte ReadRegister(byte addr)
        {
            switch(addr)
            {
                case 0x04: return (byte)fifoLevel; // FIFOLevelReg
                case 0x09: return fifoLevel > 0 ? fifoBuffer[--fifoLevel] : (byte)0; // FIFODataReg
                case 0x37: return 0x92; // VersionReg (MFRC522 v2.0)
                default: return 0;
            }
        }

        private void WriteRegister(byte addr, byte val)
        {
            switch(addr)
            {
                case 0x01: // CommandReg
                    if(val == 0x0C) // Transceive
                    {
                        // Simulate card response with TagUID
                        if(TagUID != null)
                        {
                            fifoLevel = Math.Min(TagUID.Length, 64);
                            Array.Copy(TagUID, fifoBuffer, fifoLevel);
                        }
                    }
                    break;
                case 0x09: // FIFODataReg write
                    if(fifoLevel < 64) fifoBuffer[fifoLevel++] = val;
                    break;
            }
        }

        public byte[] TagUID { get; set; } = new byte[] { 0x04, 0x11, 0x22, 0x33 };

        private byte registerAddress;
        private bool isRead;
        private int byteCount;
        private byte[] fifoBuffer;
        private int fifoIndex;
        private int fifoLevel;
    }
}
"@
Set-Content "$miscDir\MFRC522.cs" $f -Encoding UTF8

Write-Host "RFID/NFC done (2 files)."

# ══════════════════════════════════════════════════════════════
# RTC
# ══════════════════════════════════════════════════════════════

# ── DS3231 - precision RTC (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Maxim DS3231 - high-precision I2C RTC with temperature compensation
    public class DS3231 : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>
    {
        public DS3231()
        {
            RegistersCollection = new ByteRegisterCollection(this);
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0) return;
            registerAddress = data[0];
            for(var i = 1; i < data.Length; i++)
            {
                RegistersCollection.Write(registerAddress, data[i]);
                registerAddress++;
            }
        }

        public byte[] Read(int count)
        {
            UpdateTime();
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                result[i] = RegistersCollection.Read(registerAddress);
                registerAddress++;
            }
            return result;
        }

        public void FinishTransmission() { }

        public ByteRegisterCollection RegistersCollection { get; }

        private void DefineRegisters()
        {
            RegistersCollection.DefineRegister(0x00); // Seconds (BCD)
            RegistersCollection.DefineRegister(0x01); // Minutes (BCD)
            RegistersCollection.DefineRegister(0x02); // Hours (BCD)
            RegistersCollection.DefineRegister(0x03, 0x01); // Day of week
            RegistersCollection.DefineRegister(0x04, 0x01); // Date (BCD)
            RegistersCollection.DefineRegister(0x05, 0x01); // Month (BCD)
            RegistersCollection.DefineRegister(0x06, 0x25); // Year (BCD, 00-99)
            // Alarm 1 (0x07-0x0A)
            RegistersCollection.DefineRegister(0x07);
            RegistersCollection.DefineRegister(0x08);
            RegistersCollection.DefineRegister(0x09);
            RegistersCollection.DefineRegister(0x0A);
            // Alarm 2 (0x0B-0x0D)
            RegistersCollection.DefineRegister(0x0B);
            RegistersCollection.DefineRegister(0x0C);
            RegistersCollection.DefineRegister(0x0D);
            // Control (0x0E), Status (0x0F)
            RegistersCollection.DefineRegister(0x0E, 0x1C);
            RegistersCollection.DefineRegister(0x0F, 0x00);
            // Temperature (0x11-0x12), 10-bit, 0.25 C/LSB
            RegistersCollection.DefineRegister(0x11, 0x19); // 25°C integer
            RegistersCollection.DefineRegister(0x12, 0x00); // fraction
        }

        private void UpdateTime()
        {
            var now = DateTime.UtcNow;
            RegistersCollection.Write(0x00, ToBCD(now.Second));
            RegistersCollection.Write(0x01, ToBCD(now.Minute));
            RegistersCollection.Write(0x02, ToBCD(now.Hour));
            RegistersCollection.Write(0x03, (byte)((int)now.DayOfWeek + 1));
            RegistersCollection.Write(0x04, ToBCD(now.Day));
            RegistersCollection.Write(0x05, ToBCD(now.Month));
            RegistersCollection.Write(0x06, ToBCD(now.Year % 100));
        }

        private static byte ToBCD(int value)
        {
            return (byte)(((value / 10) << 4) | (value % 10));
        }

        private int registerAddress;
    }
}
"@
Set-Content "$miscDir\DS3231.cs" $f -Encoding UTF8

# ── PCF8563 - RTC (I2C) ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // NXP PCF8563 - real-time clock/calendar (I2C)
    public class PCF8563 : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>
    {
        public PCF8563()
        {
            RegistersCollection = new ByteRegisterCollection(this);
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0) return;
            registerAddress = data[0];
            for(var i = 1; i < data.Length; i++)
            {
                RegistersCollection.Write(registerAddress, data[i]);
                registerAddress = (registerAddress + 1) & 0x0F;
            }
        }

        public byte[] Read(int count)
        {
            UpdateTime();
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                result[i] = RegistersCollection.Read(registerAddress);
                registerAddress = (registerAddress + 1) & 0x0F;
            }
            return result;
        }

        public void FinishTransmission() { }

        public ByteRegisterCollection RegistersCollection { get; }

        private void DefineRegisters()
        {
            RegistersCollection.DefineRegister(0x00); // Control_status_1
            RegistersCollection.DefineRegister(0x01); // Control_status_2
            RegistersCollection.DefineRegister(0x02); // VL_seconds (BCD)
            RegistersCollection.DefineRegister(0x03); // Minutes (BCD)
            RegistersCollection.DefineRegister(0x04); // Hours (BCD)
            RegistersCollection.DefineRegister(0x05); // Days (BCD)
            RegistersCollection.DefineRegister(0x06); // Weekdays
            RegistersCollection.DefineRegister(0x07); // Century_months (BCD)
            RegistersCollection.DefineRegister(0x08); // Years (BCD)
            // Alarms 0x09-0x0C
            RegistersCollection.DefineRegister(0x09, 0x80);
            RegistersCollection.DefineRegister(0x0A, 0x80);
            RegistersCollection.DefineRegister(0x0B, 0x80);
            RegistersCollection.DefineRegister(0x0C, 0x80);
            // CLKOUT, Timer
            RegistersCollection.DefineRegister(0x0D, 0x83);
            RegistersCollection.DefineRegister(0x0E);
            RegistersCollection.DefineRegister(0x0F);
        }

        private void UpdateTime()
        {
            var now = DateTime.UtcNow;
            RegistersCollection.Write(0x02, (byte)(ToBCD(now.Second) & 0x7F));
            RegistersCollection.Write(0x03, (byte)(ToBCD(now.Minute) & 0x7F));
            RegistersCollection.Write(0x04, (byte)(ToBCD(now.Hour) & 0x3F));
            RegistersCollection.Write(0x05, (byte)(ToBCD(now.Day) & 0x3F));
            RegistersCollection.Write(0x06, (byte)((int)now.DayOfWeek & 0x07));
            RegistersCollection.Write(0x07, (byte)(ToBCD(now.Month) & 0x1F));
            RegistersCollection.Write(0x08, ToBCD(now.Year % 100));
        }

        private static byte ToBCD(int value)
        {
            return (byte)(((value / 10) << 4) | (value % 10));
        }

        private int registerAddress;
    }
}
"@
Set-Content "$miscDir\PCF8563.cs" $f -Encoding UTF8

Write-Host "RTC done (2 files)."

# ══════════════════════════════════════════════════════════════
# EEPROM / Flash
# ══════════════════════════════════════════════════════════════

# ── AT24Cxx - I2C EEPROM ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Microchip AT24Cxx - I2C EEPROM (supports AT24C02 to AT24C256)
    public class AT24Cxx : II2CPeripheral, IPeripheral
    {
        public AT24Cxx(int sizeKbits = 256)
        {
            var sizeBytes = sizeKbits * 1024 / 8;
            storage = new byte[sizeBytes];
            twoByteAddress = sizeKbits > 16;
            pageSizeMask = sizeKbits <= 16 ? 0x0F : 0x3F; // 16 or 64 byte pages
            Reset();
        }

        public void Reset()
        {
            address = 0;
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0) return;

            int dataStart;
            if(twoByteAddress)
            {
                if(data.Length < 2) return;
                address = (data[0] << 8) | data[1];
                dataStart = 2;
            }
            else
            {
                address = data[0];
                dataStart = 1;
            }

            for(var i = dataStart; i < data.Length; i++)
            {
                if(address < storage.Length)
                    storage[address] = data[i];
                // Page wrap: only lower bits increment
                address = (address & ~pageSizeMask) | ((address + 1) & pageSizeMask);
            }
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                result[i] = address < storage.Length ? storage[address] : (byte)0xFF;
                address = (address + 1) % storage.Length;
            }
            return result;
        }

        public void FinishTransmission() { }

        private byte[] storage;
        private int address;
        private bool twoByteAddress;
        private int pageSizeMask;
    }
}
"@
Set-Content "$miscDir\AT24Cxx.cs" $f -Encoding UTF8

# ── W25Qxx - SPI NOR Flash ──
$f = @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Winbond W25Qxx - SPI NOR flash (supports W25Q16 to W25Q256)
    public class W25Qxx : ISPIPeripheral, IPeripheral
    {
        public W25Qxx(int sizeMbits = 128)
        {
            var sizeBytes = sizeMbits * 1024 * 1024 / 8;
            storage = new byte[sizeBytes];
            // Fill with 0xFF (erased state)
            for(var i = 0; i < storage.Length; i++) storage[i] = 0xFF;
            // JEDEC ID: Winbond (0xEF), memory type (0x40), capacity
            byte cap;
            switch(sizeMbits)
            {
                case 16: cap = 0x15; break;
                case 32: cap = 0x16; break;
                case 64: cap = 0x17; break;
                case 128: cap = 0x18; break;
                case 256: cap = 0x19; break;
                default: cap = 0x18; break;
            }
            jedecId = new byte[] { 0xEF, 0x40, cap };
            Reset();
        }

        public void Reset()
        {
            state = State.Idle;
            address = 0;
            byteCount = 0;
            writeEnabled = false;
        }

        public void FinishTransmission()
        {
            state = State.Idle;
            byteCount = 0;
        }

        public byte Transmit(byte data)
        {
            switch(state)
            {
                case State.Idle:
                    return ProcessCommand(data);
                case State.ReadAddress:
                    address = (address << 8) | data;
                    byteCount++;
                    if(byteCount >= 3) { state = State.ReadData; }
                    return 0;
                case State.ReadData:
                    var val = address < storage.Length ? storage[address] : (byte)0xFF;
                    address++;
                    return val;
                case State.WriteAddress:
                    address = (address << 8) | data;
                    byteCount++;
                    if(byteCount >= 3) { state = State.WriteData; }
                    return 0;
                case State.WriteData:
                    if(writeEnabled && address < storage.Length)
                    {
                        storage[address] &= data; // flash can only clear bits
                        address++;
                    }
                    return 0;
                case State.ReadJedecId:
                    return byteCount < jedecId.Length ? jedecId[byteCount++] : (byte)0;
                case State.ReadStatus:
                    return (byte)(writeEnabled ? 0x02 : 0x00);
                default:
                    return 0;
            }
        }

        private byte ProcessCommand(byte cmd)
        {
            switch(cmd)
            {
                case 0x06: writeEnabled = true; break;   // WREN
                case 0x04: writeEnabled = false; break;   // WRDI
                case 0x03: // Read Data
                    state = State.ReadAddress; address = 0; byteCount = 0;
                    break;
                case 0x02: // Page Program
                    state = State.WriteAddress; address = 0; byteCount = 0;
                    break;
                case 0x9F: // JEDEC ID
                    state = State.ReadJedecId; byteCount = 0;
                    break;
                case 0x05: // Read Status Register 1
                    state = State.ReadStatus;
                    break;
                case 0x20: // Sector Erase (4KB)
                    state = State.ReadAddress; address = 0; byteCount = 0;
                    // Erase handled on CS deassert
                    break;
                case 0xC7: // Chip Erase
                    if(writeEnabled)
                    {
                        for(var i = 0; i < storage.Length; i++) storage[i] = 0xFF;
                        this.Log(LogLevel.Debug, "Chip erased");
                    }
                    break;
                case 0xAB: break; // Release Power Down
                case 0xB9: break; // Power Down
            }
            return 0;
        }

        private byte[] storage;
        private byte[] jedecId;
        private State state;
        private int address;
        private int byteCount;
        private bool writeEnabled;

        private enum State
        {
            Idle,
            ReadAddress,
            ReadData,
            WriteAddress,
            WriteData,
            ReadJedecId,
            ReadStatus
        }
    }
}
"@
Set-Content "$miscDir\W25Qxx.cs" $f -Encoding UTF8

Write-Host "EEPROM/Flash done (2 files)."
Write-Host ""
Write-Host "=== Summary ==="
$sensorCount = (Get-ChildItem "$sensorsDir\*.cs" | Measure-Object).Count
$videoCount = (Get-ChildItem "$videoDir\*.cs" | Measure-Object).Count
$miscCount = (Get-ChildItem "$miscDir\*.cs" | Measure-Object).Count
Write-Host "Sensors: $sensorCount"
Write-Host "Video/Display: $videoCount"
Write-Host "Miscellaneous: $miscCount"
Write-Host "Total: $($sensorCount + $videoCount + $miscCount)"
