$sensorsDir = "c:\GIT\renode\src\Infrastructure\src\Emulator\Peripherals\Peripherals\Sensors"
$videoDir   = "c:\GIT\renode\src\Infrastructure\src\Emulator\Peripherals\Peripherals\Video"
$miscDir    = "c:\GIT\renode\src\Infrastructure\src\Emulator\Peripherals\Peripherals\Miscellaneous"

$license = @"
//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
"@

# ─────── Helper: I2C register-based sensor ───────
function New-I2CSensor {
    param(
        [string]$ClassName,
        [string]$Comment,
        [int]$ChipId,
        [int]$ChipIdReg,
        [string]$Interfaces,       # e.g. "ITemperatureSensor, IHumiditySensor"
        [string]$Properties,       # lines for properties
        [string]$RegisterDefs,     # lines inside DefineRegisters()
        [string]$ReadoutFields,    # field declarations
        [string]$UpdateBody,       # body of UpdateReadout()
        [string]$RegisterEnum,     # enum body
        [string]$ExtraUsings = "", # additional using lines
        [string]$ExtraMethods = ""
    )

    $ifaces = "II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>"
    if($Interfaces) { $ifaces += ", $Interfaces" }

    $body = @"
$license
using System;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Utilities;
$ExtraUsings

namespace Antmicro.Renode.Peripherals.Sensors
{
    // $Comment
    public class ${ClassName} : $ifaces
    {
        public ${ClassName}()
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
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                result[i] = RegistersCollection.Read(registerAddress);
                registerAddress++;
            }
            return result;
        }

        public void FinishTransmission() { }

$Properties
        public ByteRegisterCollection RegistersCollection { get; }
$ExtraMethods

        private void DefineRegisters()
        {
$RegisterDefs
        }

        private void UpdateReadout()
        {
$UpdateBody
        }

$ReadoutFields
        private int registerAddress;

        private enum Registers : int
        {
$RegisterEnum
        }
    }
}
"@
    return $body
}

# ─────── Helper: SPI sensor ───────
function New-SPISensor {
    param(
        [string]$ClassName,
        [string]$Comment,
        [string]$Interfaces,
        [string]$Properties,
        [string]$TransmitBody,
        [string]$Fields,
        [string]$ExtraUsings = ""
    )

    $ifaces = "ISPIPeripheral"
    if($Interfaces) { $ifaces += ", $Interfaces" }

    return @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Utilities;
$ExtraUsings

namespace Antmicro.Renode.Peripherals.Sensors
{
    // $Comment
    public class ${ClassName} : $ifaces
    {
        public ${ClassName}()
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
$TransmitBody
        }

$Properties

$Fields
        private int byteIndex;
    }
}
"@
}

# ─────── Helper: I2C command-based sensor (like SHT/SGP/SCD) ───────
function New-CmdSensor {
    param(
        [string]$ClassName,
        [string]$Comment,
        [string]$Interfaces,
        [string]$Properties,
        [string]$WriteBody,
        [string]$Fields,
        [string]$ExtraMethods = ""
    )

    $ifaces = "II2CPeripheral"
    if($Interfaces) { $ifaces += ", $Interfaces" }

    return @"
$license
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Sensors
{
    // $Comment
    public class ${ClassName} : $ifaces
    {
        public ${ClassName}()
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
            if(data.Length == 0) return;
$WriteBody
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            for(var i = 0; i < count && readIndex < readBuffer.Length; i++)
                result[i] = readBuffer[readIndex++];
            return result;
        }

        public void FinishTransmission() { }

$Properties
$ExtraMethods

        private byte[] readBuffer;
        private int readIndex;
$Fields
    }
}
"@
}

Write-Host "Generating sensor files..."

# ══════════════════════════════════════════════════════════════
# 1. AHT20 – command-based humidity/temp
# ══════════════════════════════════════════════════════════════
$f = New-CmdSensor -ClassName "AHT20" `
    -Comment "Asair AHT20 - humidity and temperature sensor (I2C)" `
    -Interfaces "ITemperatureSensor, IHumiditySensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Humidity { get; set; } = 50.0m;
"@ `
    -WriteBody @"
            if(data[0] == 0xAC && data.Length >= 3)
            {
                // Trigger measurement
                var rawH = (uint)(Humidity / 100m * 0x100000);
                var rawT = (uint)((Temperature + 50m) / 200m * 0x100000);
                readBuffer = new byte[]
                {
                    0x1C, // status: calibrated, not busy
                    (byte)((rawH >> 12) & 0xFF),
                    (byte)((rawH >> 4) & 0xFF),
                    (byte)(((rawH & 0x0F) << 4) | ((rawT >> 16) & 0x0F)),
                    (byte)((rawT >> 8) & 0xFF),
                    (byte)(rawT & 0xFF),
                    0x00  // CRC placeholder
                };
                readIndex = 0;
            }
            else if(data[0] == 0xBA) { Reset(); } // soft reset
"@
Set-Content "$sensorsDir\AHT20.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 2. HTU21D
# ══════════════════════════════════════════════════════════════
$f = New-CmdSensor -ClassName "HTU21D" `
    -Comment "TE Connectivity HTU21D - humidity and temperature sensor (I2C)" `
    -Interfaces "ITemperatureSensor, IHumiditySensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Humidity { get; set; } = 50.0m;
"@ `
    -WriteBody @"
            switch(data[0])
            {
                case 0xE3: // Trigger temp measurement (hold master)
                case 0xF3: // Trigger temp measurement (no hold)
                    var rawT = (ushort)(Temperature / 175.72m * 65536m + 46.85m / 175.72m * 65536m);
                    readBuffer = new byte[] { (byte)(rawT >> 8), (byte)(rawT & 0xFC), 0x00 };
                    readIndex = 0;
                    break;
                case 0xE5: // Trigger humidity (hold master)
                case 0xF5: // Trigger humidity (no hold)
                    var rawH = (ushort)(Humidity / 125m * 65536m + 6m / 125m * 65536m);
                    readBuffer = new byte[] { (byte)(rawH >> 8), (byte)(rawH & 0xFC), 0x00 };
                    readIndex = 0;
                    break;
                case 0xFE: // Soft reset
                    Reset();
                    break;
            }
"@
Set-Content "$sensorsDir\HTU21D.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 3. MCP9808 – precision temp (I2C register-based)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "MCP9808" `
    -Comment "Microchip MCP9808 - high-accuracy temperature sensor (I2C)" `
    -ChipId 0x04 -ChipIdReg 0x07 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
"@ `
    -RegisterDefs @"
            Registers.Config.Define(this, 0x00);
            Registers.TUpper.Define(this, 0x00);
            Registers.TLower.Define(this, 0x00);
            Registers.TCrit.Define(this, 0x00);
            Registers.TAmbientMSB.Define(this).WithValueField(0, 8, out taMSB, FieldMode.Read);
            Registers.TAmbientLSB.Define(this).WithValueField(0, 8, out taLSB, FieldMode.Read);
            Registers.ManufacturerId.Define(this, 0x00).WithValueField(0, 8, FieldMode.Read, valueProviderCallback: _ => 0x54);
            Registers.DeviceId.Define(this, 0x04).WithValueField(0, 8, FieldMode.Read, valueProviderCallback: _ => 0x04);
            Registers.Resolution.Define(this, 0x03);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // 13-bit signed value, 0.0625 C/LSB, bits [12]=sign, [11:4]=integer, [3:0]=fraction
            var raw = (short)(Temperature / 0.0625m);
            var upper = (byte)((raw >> 8) & 0x1F);
            if(Temperature < 0) upper |= 0x10;
            taMSB.Value = upper;
            taLSB.Value = (byte)(raw & 0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField taMSB, taLSB;
"@ `
    -RegisterEnum @"
            Config = 0x01,
            TUpper = 0x02,
            TLower = 0x03,
            TCrit  = 0x04,
            TAmbientMSB = 0x05,
            TAmbientLSB = 0x06,
            ManufacturerId = 0x06,
            DeviceId = 0x07,
            Resolution = 0x08,
"@
Set-Content "$sensorsDir\MCP9808.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 4. TMP117 – precision temp (I2C)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "TMP117" `
    -Comment "TI TMP117 - high-precision temperature sensor (I2C), 16-bit 0.0078125 C/LSB" `
    -ChipId 0x17 -ChipIdReg 0x0F `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
"@ `
    -RegisterDefs @"
            Registers.TempResultMSB.Define(this).WithValueField(0, 8, out tempMSB, FieldMode.Read);
            Registers.TempResultLSB.Define(this).WithValueField(0, 8, out tempLSB, FieldMode.Read);
            Registers.ConfigurationMSB.Define(this, 0x02);
            Registers.ConfigurationLSB.Define(this, 0x20);
            Registers.DeviceIdMSB.Define(this, 0x01);
            Registers.DeviceIdLSB.Define(this, 0x17);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var raw = (short)(Temperature / 0.0078125m);
            tempMSB.Value = (byte)((raw >> 8) & 0xFF);
            tempLSB.Value = (byte)(raw & 0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField tempMSB, tempLSB;
"@ `
    -RegisterEnum @"
            TempResultMSB = 0x00,
            TempResultLSB = 0x01,
            ConfigurationMSB = 0x02,
            ConfigurationLSB = 0x03,
            DeviceIdMSB = 0x0E,
            DeviceIdLSB = 0x0F,
"@
Set-Content "$sensorsDir\TMP117.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 5. LPS22HB – pressure sensor (I2C)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "LPS22HB" `
    -Comment "ST LPS22HB - MEMS pressure sensor (I2C), WHO_AM_I=0xB1" `
    -ChipId 0xB1 -ChipIdReg 0x0F `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Pressure { get; set; } = 1013.25m;
"@ `
    -RegisterDefs @"
            Registers.WhoAmI.Define(this, 0xB1);
            Registers.CtrlReg1.Define(this).WithValueField(0, 8, name: ""ctrl_reg1"");
            Registers.CtrlReg2.Define(this).WithValueField(0, 8, name: ""ctrl_reg2"")
                .WithWriteCallback((_, val) => { if((val & 0x04) != 0) Reset(); if((val & 0x01) != 0) UpdateReadout(); });
            Registers.StatusReg.Define(this, 0x03);
            Registers.PressOutXL.Define(this).WithValueField(0, 8, out pXL, FieldMode.Read);
            Registers.PressOutL.Define(this).WithValueField(0, 8, out pL, FieldMode.Read);
            Registers.PressOutH.Define(this).WithValueField(0, 8, out pH, FieldMode.Read);
            Registers.TempOutL.Define(this).WithValueField(0, 8, out tL, FieldMode.Read);
            Registers.TempOutH.Define(this).WithValueField(0, 8, out tH, FieldMode.Read);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // Pressure in hPa, output is 4096 LSB/hPa (24-bit)
            var rawP = (int)(Pressure * 4096m);
            pXL.Value = (byte)(rawP & 0xFF);
            pL.Value  = (byte)((rawP >> 8) & 0xFF);
            pH.Value  = (byte)((rawP >> 16) & 0xFF);
            // Temperature: 100 LSB/C (16-bit signed)
            var rawT = (short)(Temperature * 100m);
            tL.Value = (byte)(rawT & 0xFF);
            tH.Value = (byte)((rawT >> 8) & 0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField pXL, pL, pH, tL, tH;
"@ `
    -RegisterEnum @"
            WhoAmI = 0x0F,
            CtrlReg1 = 0x10,
            CtrlReg2 = 0x11,
            StatusReg = 0x27,
            PressOutXL = 0x28,
            PressOutL = 0x29,
            PressOutH = 0x2A,
            TempOutL = 0x2B,
            TempOutH = 0x2C,
"@
Set-Content "$sensorsDir\LPS22HB.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 6. MS5611 – barometric pressure (I2C command-based)
# ══════════════════════════════════════════════════════════════
$f = New-CmdSensor -ClassName "MS5611" `
    -Comment "TE Connectivity MS5611 - barometric pressure sensor (I2C)" `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Pressure { get; set; } = 1013.25m;
"@ `
    -WriteBody @"
            var cmd = data[0];
            if(cmd == 0x1E) { Reset(); } // Reset command
            else if(cmd >= 0x40 && cmd <= 0x4E) // Convert D1 (pressure)
            {
                var rawP = (uint)(Pressure / 1013.25m * 6000000);
                readBuffer = new byte[] { (byte)((rawP >> 16) & 0xFF), (byte)((rawP >> 8) & 0xFF), (byte)(rawP & 0xFF) };
                readIndex = 0;
            }
            else if(cmd >= 0x50 && cmd <= 0x5E) // Convert D2 (temperature)
            {
                var rawT = (uint)((Temperature + 40m) / 85m * 8000000);
                readBuffer = new byte[] { (byte)((rawT >> 16) & 0xFF), (byte)((rawT >> 8) & 0xFF), (byte)(rawT & 0xFF) };
                readIndex = 0;
            }
            else if(cmd == 0x00) { /* ADC read - data already prepared */ }
            else if(cmd >= 0xA0 && cmd <= 0xAE) // PROM read
            {
                readBuffer = new byte[] { 0x00, 0x80 };
                readIndex = 0;
            }
"@
Set-Content "$sensorsDir\MS5611.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 7. DPS310 – pressure sensor (I2C)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "DPS310" `
    -Comment "Infineon DPS310 - barometric pressure sensor (I2C), Product ID=0x10" `
    -ChipId 0x10 -ChipIdReg 0x0D `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Pressure { get; set; } = 1013.25m;
"@ `
    -RegisterDefs @"
            Registers.PsrB2.Define(this).WithValueField(0, 8, out pB2, FieldMode.Read);
            Registers.PsrB1.Define(this).WithValueField(0, 8, out pB1, FieldMode.Read);
            Registers.PsrB0.Define(this).WithValueField(0, 8, out pB0, FieldMode.Read);
            Registers.TmpB2.Define(this).WithValueField(0, 8, out tB2, FieldMode.Read);
            Registers.TmpB1.Define(this).WithValueField(0, 8, out tB1, FieldMode.Read);
            Registers.TmpB0.Define(this).WithValueField(0, 8, out tB0, FieldMode.Read);
            Registers.PrsCfg.Define(this);
            Registers.TmpCfg.Define(this, 0x80);
            Registers.MeasCfg.Define(this, 0xC0)
                .WithWriteCallback((_, __) => UpdateReadout());
            Registers.CfgReg.Define(this);
            Registers.ProductId.Define(this, 0x10);
            Registers.CoefSrc.Define(this, 0x80);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var rawP = (int)(Pressure * 100m);
            pB2.Value = (byte)((rawP >> 16) & 0xFF);
            pB1.Value = (byte)((rawP >> 8) & 0xFF);
            pB0.Value = (byte)(rawP & 0xFF);
            var rawT = (int)(Temperature * 100m);
            tB2.Value = (byte)((rawT >> 16) & 0xFF);
            tB1.Value = (byte)((rawT >> 8) & 0xFF);
            tB0.Value = (byte)(rawT & 0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField pB2, pB1, pB0, tB2, tB1, tB0;
"@ `
    -RegisterEnum @"
            PsrB2 = 0x00,
            PsrB1 = 0x01,
            PsrB0 = 0x02,
            TmpB2 = 0x03,
            TmpB1 = 0x04,
            TmpB0 = 0x05,
            PrsCfg = 0x06,
            TmpCfg = 0x07,
            MeasCfg = 0x08,
            CfgReg = 0x09,
            ProductId = 0x0D,
            CoefSrc = 0x28,
"@
Set-Content "$sensorsDir\DPS310.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 8. MPU6050 – 6-axis IMU (I2C)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "MPU6050" `
    -Comment "InvenSense MPU-6050 - 6-axis IMU (I2C), WHO_AM_I=0x68" `
    -ChipId 0x68 -ChipIdReg 0x75 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.SmprtDiv.Define(this);
            Registers.Config.Define(this);
            Registers.GyroConfig.Define(this);
            Registers.AccelConfig.Define(this);
            Registers.IntEnable.Define(this);
            Registers.IntStatus.Define(this, 0x01);
            Registers.AccelXOutH.Define(this).WithValueField(0, 8, out axH, FieldMode.Read);
            Registers.AccelXOutL.Define(this).WithValueField(0, 8, out axL, FieldMode.Read);
            Registers.AccelYOutH.Define(this).WithValueField(0, 8, out ayH, FieldMode.Read);
            Registers.AccelYOutL.Define(this).WithValueField(0, 8, out ayL, FieldMode.Read);
            Registers.AccelZOutH.Define(this).WithValueField(0, 8, out azH, FieldMode.Read);
            Registers.AccelZOutL.Define(this).WithValueField(0, 8, out azL, FieldMode.Read);
            Registers.TempOutH.Define(this).WithValueField(0, 8, out tH, FieldMode.Read);
            Registers.TempOutL.Define(this).WithValueField(0, 8, out tL, FieldMode.Read);
            Registers.GyroXOutH.Define(this).WithValueField(0, 8, out gxH, FieldMode.Read);
            Registers.GyroXOutL.Define(this).WithValueField(0, 8, out gxL, FieldMode.Read);
            Registers.GyroYOutH.Define(this).WithValueField(0, 8, out gyH, FieldMode.Read);
            Registers.GyroYOutL.Define(this).WithValueField(0, 8, out gyL, FieldMode.Read);
            Registers.GyroZOutH.Define(this).WithValueField(0, 8, out gzH, FieldMode.Read);
            Registers.GyroZOutL.Define(this).WithValueField(0, 8, out gzL, FieldMode.Read);
            Registers.PwrMgmt1.Define(this, 0x40);
            Registers.PwrMgmt2.Define(this);
            Registers.WhoAmI.Define(this, 0x68);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // Accel: +/-2g default => 16384 LSB/g
            var ax = (short)(AccelerationX * 16384m); var ay = (short)(AccelerationY * 16384m); var az = (short)(AccelerationZ * 16384m);
            axH.Value = (byte)(ax >> 8); axL.Value = (byte)(ax & 0xFF);
            ayH.Value = (byte)(ay >> 8); ayL.Value = (byte)(ay & 0xFF);
            azH.Value = (byte)(az >> 8); azL.Value = (byte)(az & 0xFF);
            // Gyro: +/-250 dps default => 131 LSB/(deg/s)
            var gx = (short)(AngularRateX * 131m); var gy = (short)(AngularRateY * 131m); var gz = (short)(AngularRateZ * 131m);
            gxH.Value = (byte)(gx >> 8); gxL.Value = (byte)(gx & 0xFF);
            gyH.Value = (byte)(gy >> 8); gyL.Value = (byte)(gy & 0xFF);
            gzH.Value = (byte)(gz >> 8); gzL.Value = (byte)(gz & 0xFF);
            // Temp: T_degC = (raw/340) + 36.53
            var rawT = (short)((Temperature - 36.53m) * 340m);
            tH.Value = (byte)(rawT >> 8); tL.Value = (byte)(rawT & 0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField axH, axL, ayH, ayL, azH, azL;
        private IValueRegisterField tH, tL;
        private IValueRegisterField gxH, gxL, gyH, gyL, gzH, gzL;
"@ `
    -RegisterEnum @"
            SmprtDiv = 0x19, Config = 0x1A, GyroConfig = 0x1B, AccelConfig = 0x1C,
            IntEnable = 0x38, IntStatus = 0x3A,
            AccelXOutH = 0x3B, AccelXOutL = 0x3C,
            AccelYOutH = 0x3D, AccelYOutL = 0x3E,
            AccelZOutH = 0x3F, AccelZOutL = 0x40,
            TempOutH = 0x41, TempOutL = 0x42,
            GyroXOutH = 0x43, GyroXOutL = 0x44,
            GyroYOutH = 0x45, GyroYOutL = 0x46,
            GyroZOutH = 0x47, GyroZOutL = 0x48,
            PwrMgmt1 = 0x6B, PwrMgmt2 = 0x6C,
            WhoAmI = 0x75,
"@
Set-Content "$sensorsDir\MPU6050.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 9. MPU9250 – 9-axis IMU (I2C) – register-compatible with MPU6050 base
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "MPU9250" `
    -Comment "InvenSense MPU-9250 - 9-axis IMU (I2C), WHO_AM_I=0x71" `
    -ChipId 0x71 -ChipIdReg 0x75 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.SmprtDiv.Define(this);
            Registers.Config.Define(this);
            Registers.GyroConfig.Define(this);
            Registers.AccelConfig.Define(this);
            Registers.AccelConfig2.Define(this);
            Registers.IntEnable.Define(this);
            Registers.IntStatus.Define(this, 0x01);
            Registers.AccelXOutH.Define(this).WithValueField(0, 8, out axH, FieldMode.Read);
            Registers.AccelXOutL.Define(this).WithValueField(0, 8, out axL, FieldMode.Read);
            Registers.AccelYOutH.Define(this).WithValueField(0, 8, out ayH, FieldMode.Read);
            Registers.AccelYOutL.Define(this).WithValueField(0, 8, out ayL, FieldMode.Read);
            Registers.AccelZOutH.Define(this).WithValueField(0, 8, out azH, FieldMode.Read);
            Registers.AccelZOutL.Define(this).WithValueField(0, 8, out azL, FieldMode.Read);
            Registers.TempOutH.Define(this).WithValueField(0, 8, out tH, FieldMode.Read);
            Registers.TempOutL.Define(this).WithValueField(0, 8, out tL, FieldMode.Read);
            Registers.GyroXOutH.Define(this).WithValueField(0, 8, out gxH, FieldMode.Read);
            Registers.GyroXOutL.Define(this).WithValueField(0, 8, out gxL, FieldMode.Read);
            Registers.GyroYOutH.Define(this).WithValueField(0, 8, out gyH, FieldMode.Read);
            Registers.GyroYOutL.Define(this).WithValueField(0, 8, out gyL, FieldMode.Read);
            Registers.GyroZOutH.Define(this).WithValueField(0, 8, out gzH, FieldMode.Read);
            Registers.GyroZOutL.Define(this).WithValueField(0, 8, out gzL, FieldMode.Read);
            Registers.PwrMgmt1.Define(this, 0x01);
            Registers.WhoAmI.Define(this, 0x71);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var ax = (short)(AccelerationX * 16384m); var ay = (short)(AccelerationY * 16384m); var az = (short)(AccelerationZ * 16384m);
            axH.Value = (byte)(ax >> 8); axL.Value = (byte)(ax & 0xFF);
            ayH.Value = (byte)(ay >> 8); ayL.Value = (byte)(ay & 0xFF);
            azH.Value = (byte)(az >> 8); azL.Value = (byte)(az & 0xFF);
            var gx = (short)(AngularRateX * 131m); var gy = (short)(AngularRateY * 131m); var gz = (short)(AngularRateZ * 131m);
            gxH.Value = (byte)(gx >> 8); gxL.Value = (byte)(gx & 0xFF);
            gyH.Value = (byte)(gy >> 8); gyL.Value = (byte)(gy & 0xFF);
            gzH.Value = (byte)(gz >> 8); gzL.Value = (byte)(gz & 0xFF);
            var rawT = (short)((Temperature - 21m) * 333.87m);
            tH.Value = (byte)(rawT >> 8); tL.Value = (byte)(rawT & 0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField axH, axL, ayH, ayL, azH, azL;
        private IValueRegisterField tH, tL;
        private IValueRegisterField gxH, gxL, gyH, gyL, gzH, gzL;
"@ `
    -RegisterEnum @"
            SmprtDiv = 0x19, Config = 0x1A, GyroConfig = 0x1B, AccelConfig = 0x1C, AccelConfig2 = 0x1D,
            IntEnable = 0x38, IntStatus = 0x3A,
            AccelXOutH = 0x3B, AccelXOutL = 0x3C, AccelYOutH = 0x3D, AccelYOutL = 0x3E,
            AccelZOutH = 0x3F, AccelZOutL = 0x40,
            TempOutH = 0x41, TempOutL = 0x42,
            GyroXOutH = 0x43, GyroXOutL = 0x44, GyroYOutH = 0x45, GyroYOutL = 0x46,
            GyroZOutH = 0x47, GyroZOutL = 0x48,
            PwrMgmt1 = 0x6B,
            WhoAmI = 0x75,
"@
Set-Content "$sensorsDir\MPU9250.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 10-11. BMI160 / BMI270 – 6-axis IMU (I2C)
# ══════════════════════════════════════════════════════════════
foreach($pair in @(
    @{N="BMI160"; Id=0xD1; Comment="Bosch BMI160 - 6-axis IMU (I2C), Chip ID=0xD1"},
    @{N="BMI270"; Id=0x24; Comment="Bosch BMI270 - 6-axis IMU (I2C), Chip ID=0x24"}
)) {
$f = New-I2CSensor -ClassName $pair.N `
    -Comment $pair.Comment -ChipId $pair.Id -ChipIdReg 0x00 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.ChipId.Define(this, $("0x{0:X2}" -f $pair.Id));
            Registers.Status.Define(this, 0xF0);
            Registers.AccXL.Define(this).WithValueField(0, 8, out axL, FieldMode.Read);
            Registers.AccXH.Define(this).WithValueField(0, 8, out axH, FieldMode.Read);
            Registers.AccYL.Define(this).WithValueField(0, 8, out ayL, FieldMode.Read);
            Registers.AccYH.Define(this).WithValueField(0, 8, out ayH, FieldMode.Read);
            Registers.AccZL.Define(this).WithValueField(0, 8, out azL, FieldMode.Read);
            Registers.AccZH.Define(this).WithValueField(0, 8, out azH, FieldMode.Read);
            Registers.GyrXL.Define(this).WithValueField(0, 8, out gxL, FieldMode.Read);
            Registers.GyrXH.Define(this).WithValueField(0, 8, out gxH, FieldMode.Read);
            Registers.GyrYL.Define(this).WithValueField(0, 8, out gyL, FieldMode.Read);
            Registers.GyrYH.Define(this).WithValueField(0, 8, out gyH, FieldMode.Read);
            Registers.GyrZL.Define(this).WithValueField(0, 8, out gzL, FieldMode.Read);
            Registers.GyrZH.Define(this).WithValueField(0, 8, out gzH, FieldMode.Read);
            Registers.TempL.Define(this).WithValueField(0, 8, out tL, FieldMode.Read);
            Registers.TempH.Define(this).WithValueField(0, 8, out tH, FieldMode.Read);
            Registers.Cmd.Define(this).WithWriteCallback((_, val) => { if(val == 0xB6) Reset(); });
            UpdateReadout();
"@ `
    -UpdateBody @"
            var ax = (short)(AccelerationX * 16384m); axL.Value=(byte)(ax&0xFF); axH.Value=(byte)(ax>>8);
            var ay = (short)(AccelerationY * 16384m); ayL.Value=(byte)(ay&0xFF); ayH.Value=(byte)(ay>>8);
            var az = (short)(AccelerationZ * 16384m); azL.Value=(byte)(az&0xFF); azH.Value=(byte)(az>>8);
            var gx = (short)(AngularRateX * 131m); gxL.Value=(byte)(gx&0xFF); gxH.Value=(byte)(gx>>8);
            var gy = (short)(AngularRateY * 131m); gyL.Value=(byte)(gy&0xFF); gyH.Value=(byte)(gy>>8);
            var gz = (short)(AngularRateZ * 131m); gzL.Value=(byte)(gz&0xFF); gzH.Value=(byte)(gz>>8);
            var rawT = (short)((Temperature - 23m) * 512m);
            tL.Value=(byte)(rawT&0xFF); tH.Value=(byte)(rawT>>8);
"@ `
    -ReadoutFields @"
        private IValueRegisterField axL,axH,ayL,ayH,azL,azH,gxL,gxH,gyL,gyH,gzL,gzH,tL,tH;
"@ `
    -RegisterEnum @"
            ChipId=0x00, Status=0x1B,
            AccXL=0x12,AccXH=0x13,AccYL=0x14,AccYH=0x15,AccZL=0x16,AccZH=0x17,
            GyrXL=0x0C,GyrXH=0x0D,GyrYL=0x0E,GyrYH=0x0F,GyrZL=0x10,GyrZH=0x11,
            TempL=0x20,TempH=0x21,
            Cmd=0x7E,
"@
Set-Content "$sensorsDir\$($pair.N).cs" $f -Encoding UTF8
}

# ══════════════════════════════════════════════════════════════
# 12. BNO055 – 9DOF fusion (I2C)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "BNO055" `
    -Comment "Bosch BNO055 - 9-axis absolute orientation sensor (I2C), Chip ID=0xA0" `
    -ChipId 0xA0 -ChipIdReg 0x00 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
        public decimal EulerHeading { get; set; }
        public decimal EulerRoll { get; set; }
        public decimal EulerPitch { get; set; }
"@ `
    -RegisterDefs @"
            Registers.ChipId.Define(this, 0xA0);
            Registers.AccId.Define(this, 0xFB);
            Registers.MagId.Define(this, 0x32);
            Registers.GyrId.Define(this, 0x0F);
            Registers.SwRevLSB.Define(this, 0x08);
            Registers.SwRevMSB.Define(this, 0x03);
            Registers.PageId.Define(this);
            Registers.AccXL.Define(this).WithValueField(0,8,out axL,FieldMode.Read);
            Registers.AccXH.Define(this).WithValueField(0,8,out axH,FieldMode.Read);
            Registers.AccYL.Define(this).WithValueField(0,8,out ayL,FieldMode.Read);
            Registers.AccYH.Define(this).WithValueField(0,8,out ayH,FieldMode.Read);
            Registers.AccZL.Define(this).WithValueField(0,8,out azL,FieldMode.Read);
            Registers.AccZH.Define(this).WithValueField(0,8,out azH,FieldMode.Read);
            Registers.GyrXL.Define(this).WithValueField(0,8,out gxL,FieldMode.Read);
            Registers.GyrXH.Define(this).WithValueField(0,8,out gxH,FieldMode.Read);
            Registers.GyrYL.Define(this).WithValueField(0,8,out gyL,FieldMode.Read);
            Registers.GyrYH.Define(this).WithValueField(0,8,out gyH,FieldMode.Read);
            Registers.GyrZL.Define(this).WithValueField(0,8,out gzL,FieldMode.Read);
            Registers.GyrZH.Define(this).WithValueField(0,8,out gzH,FieldMode.Read);
            Registers.EulHL.Define(this).WithValueField(0,8,out ehL,FieldMode.Read);
            Registers.EulHH.Define(this).WithValueField(0,8,out ehH,FieldMode.Read);
            Registers.EulRL.Define(this).WithValueField(0,8,out erL,FieldMode.Read);
            Registers.EulRH.Define(this).WithValueField(0,8,out erH,FieldMode.Read);
            Registers.EulPL.Define(this).WithValueField(0,8,out epL,FieldMode.Read);
            Registers.EulPH.Define(this).WithValueField(0,8,out epH,FieldMode.Read);
            Registers.TempReg.Define(this).WithValueField(0,8,out tempReg,FieldMode.Read);
            Registers.CalibStat.Define(this, 0xFF);
            Registers.SysStat.Define(this, 0x05);
            Registers.OprMode.Define(this);
            Registers.SysTrigger.Define(this).WithWriteCallback((_,val) => { if((val & 0x20) != 0) Reset(); });
            UpdateReadout();
"@ `
    -UpdateBody @"
            var ax = (short)(AccelerationX * 100m); axL.Value=(byte)(ax&0xFF); axH.Value=(byte)(ax>>8);
            var ay = (short)(AccelerationY * 100m); ayL.Value=(byte)(ay&0xFF); ayH.Value=(byte)(ay>>8);
            var az = (short)(AccelerationZ * 100m); azL.Value=(byte)(az&0xFF); azH.Value=(byte)(az>>8);
            var gx = (short)(AngularRateX * 16m); gxL.Value=(byte)(gx&0xFF); gxH.Value=(byte)(gx>>8);
            var gy = (short)(AngularRateY * 16m); gyL.Value=(byte)(gy&0xFF); gyH.Value=(byte)(gy>>8);
            var gz = (short)(AngularRateZ * 16m); gzL.Value=(byte)(gz&0xFF); gzH.Value=(byte)(gz>>8);
            var eh = (short)(EulerHeading * 16m); ehL.Value=(byte)(eh&0xFF); ehH.Value=(byte)(eh>>8);
            var er = (short)(EulerRoll * 16m); erL.Value=(byte)(er&0xFF); erH.Value=(byte)(er>>8);
            var ep = (short)(EulerPitch * 16m); epL.Value=(byte)(ep&0xFF); epH.Value=(byte)(ep>>8);
            tempReg.Value = (byte)(sbyte)Temperature;
"@ `
    -ReadoutFields @"
        private IValueRegisterField axL,axH,ayL,ayH,azL,azH,gxL,gxH,gyL,gyH,gzL,gzH;
        private IValueRegisterField ehL,ehH,erL,erH,epL,epH,tempReg;
"@ `
    -RegisterEnum @"
            ChipId=0x00,AccId=0x01,MagId=0x02,GyrId=0x03,SwRevLSB=0x04,SwRevMSB=0x05,PageId=0x07,
            AccXL=0x08,AccXH=0x09,AccYL=0x0A,AccYH=0x0B,AccZL=0x0C,AccZH=0x0D,
            GyrXL=0x14,GyrXH=0x15,GyrYL=0x16,GyrYH=0x17,GyrZL=0x18,GyrZH=0x19,
            EulHL=0x1A,EulHH=0x1B,EulRL=0x1C,EulRH=0x1D,EulPL=0x1E,EulPH=0x1F,
            TempReg=0x34,CalibStat=0x35,SysStat=0x39,
            OprMode=0x3D,SysTrigger=0x3F,
"@
Set-Content "$sensorsDir\BNO055.cs" $f -Encoding UTF8

# ══════════════════════════════════════════════════════════════
# 13. ICM42688P – 6-axis IMU
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "ICM42688P" `
    -Comment "TDK ICM-42688-P - high-performance 6-axis IMU (I2C), WHO_AM_I=0x47" `
    -ChipId 0x47 -ChipIdReg 0x75 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.IntStatus.Define(this, 0x08);
            Registers.TempDataH.Define(this).WithValueField(0,8,out tH,FieldMode.Read);
            Registers.TempDataL.Define(this).WithValueField(0,8,out tL,FieldMode.Read);
            Registers.AccelXH.Define(this).WithValueField(0,8,out axH,FieldMode.Read);
            Registers.AccelXL.Define(this).WithValueField(0,8,out axL,FieldMode.Read);
            Registers.AccelYH.Define(this).WithValueField(0,8,out ayH,FieldMode.Read);
            Registers.AccelYL.Define(this).WithValueField(0,8,out ayL,FieldMode.Read);
            Registers.AccelZH.Define(this).WithValueField(0,8,out azH,FieldMode.Read);
            Registers.AccelZL.Define(this).WithValueField(0,8,out azL,FieldMode.Read);
            Registers.GyroXH.Define(this).WithValueField(0,8,out gxH,FieldMode.Read);
            Registers.GyroXL.Define(this).WithValueField(0,8,out gxL,FieldMode.Read);
            Registers.GyroYH.Define(this).WithValueField(0,8,out gyH,FieldMode.Read);
            Registers.GyroYL.Define(this).WithValueField(0,8,out gyL,FieldMode.Read);
            Registers.GyroZH.Define(this).WithValueField(0,8,out gzH,FieldMode.Read);
            Registers.GyroZL.Define(this).WithValueField(0,8,out gzL,FieldMode.Read);
            Registers.PwrMgmt0.Define(this);
            Registers.WhoAmI.Define(this, 0x47);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var ax=(short)(AccelerationX*16384m); axH.Value=(byte)(ax>>8); axL.Value=(byte)(ax&0xFF);
            var ay=(short)(AccelerationY*16384m); ayH.Value=(byte)(ay>>8); ayL.Value=(byte)(ay&0xFF);
            var az=(short)(AccelerationZ*16384m); azH.Value=(byte)(az>>8); azL.Value=(byte)(az&0xFF);
            var gx=(short)(AngularRateX*131m); gxH.Value=(byte)(gx>>8); gxL.Value=(byte)(gx&0xFF);
            var gy=(short)(AngularRateY*131m); gyH.Value=(byte)(gy>>8); gyL.Value=(byte)(gy&0xFF);
            var gz=(short)(AngularRateZ*131m); gzH.Value=(byte)(gz>>8); gzL.Value=(byte)(gz&0xFF);
            var rawT=(short)((Temperature-25m)*132.48m+25m*132.48m);
            tH.Value=(byte)(rawT>>8); tL.Value=(byte)(rawT&0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField axH,axL,ayH,ayL,azH,azL,gxH,gxL,gyH,gyL,gzH,gzL,tH,tL;
"@ `
    -RegisterEnum @"
            IntStatus=0x2D,
            TempDataH=0x1D,TempDataL=0x1E,
            AccelXH=0x1F,AccelXL=0x20,AccelYH=0x21,AccelYL=0x22,AccelZH=0x23,AccelZL=0x24,
            GyroXH=0x25,GyroXL=0x26,GyroYH=0x27,GyroYL=0x28,GyroZH=0x29,GyroZL=0x2A,
            PwrMgmt0=0x4E,
            WhoAmI=0x75,
"@
Set-Content "$sensorsDir\ICM42688P.cs" $f -Encoding UTF8

Write-Host "Batch 1 done (13 sensors)."

# ══════════════════════════════════════════════════════════════
# 14-17: LIS3DH, LIS2DH12, MMA8451, FXOS8700
# ══════════════════════════════════════════════════════════════
foreach($s in @(
    @{N="LIS3DH";   Id=0x33; Comment="ST LIS3DH - 3-axis accelerometer (I2C)"; IdReg=0x0F},
    @{N="LIS2DH12"; Id=0x33; Comment="ST LIS2DH12 - 3-axis accelerometer (I2C)"; IdReg=0x0F},
    @{N="MMA8451";  Id=0x1A; Comment="NXP MMA8451Q - 3-axis accelerometer (I2C)"; IdReg=0x0D},
    @{N="FXOS8700"; Id=0xC7; Comment="NXP FXOS8700CQ - 6-axis accel+mag (I2C)"; IdReg=0x0D}
)) {
$f = New-I2CSensor -ClassName $s.N -Comment $s.Comment -ChipId $s.Id -ChipIdReg $s.IdReg `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
"@ `
    -RegisterDefs @"
            Registers.WhoAmI.Define(this, $("0x{0:X2}" -f $s.Id));
            Registers.Status.Define(this, 0x0F);
            Registers.OutXH.Define(this).WithValueField(0,8,out axH,FieldMode.Read);
            Registers.OutXL.Define(this).WithValueField(0,8,out axL,FieldMode.Read);
            Registers.OutYH.Define(this).WithValueField(0,8,out ayH,FieldMode.Read);
            Registers.OutYL.Define(this).WithValueField(0,8,out ayL,FieldMode.Read);
            Registers.OutZH.Define(this).WithValueField(0,8,out azH,FieldMode.Read);
            Registers.OutZL.Define(this).WithValueField(0,8,out azL,FieldMode.Read);
            Registers.CtrlReg1.Define(this);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // 14-bit, +/-2g default => 4096 LSB/g
            var ax=(short)(AccelerationX*4096m); axH.Value=(byte)(ax>>8); axL.Value=(byte)(ax&0xFF);
            var ay=(short)(AccelerationY*4096m); ayH.Value=(byte)(ay>>8); ayL.Value=(byte)(ay&0xFF);
            var az=(short)(AccelerationZ*4096m); azH.Value=(byte)(az>>8); azL.Value=(byte)(az&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField axH,axL,ayH,ayL,azH,azL;" `
    -RegisterEnum @"
            WhoAmI=$("0x{0:X2}" -f $s.IdReg),
            Status=0x27,
            OutXH=0x29,OutXL=0x28,OutYH=0x2B,OutYL=0x2A,OutZH=0x2D,OutZL=0x2C,
            CtrlReg1=0x20,
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# ══════════════════════════════════════════════════════════════
# 18-19: LSM6DSL, ISM330DHCX
# ══════════════════════════════════════════════════════════════
foreach($s in @(
    @{N="LSM6DSL";    Id=0x6A; Comment="ST LSM6DSL - 6-axis IMU (I2C)"},
    @{N="ISM330DHCX"; Id=0x6B; Comment="ST ISM330DHCX - 6-axis IMU (I2C)"}
)) {
$f = New-I2CSensor -ClassName $s.N -Comment $s.Comment -ChipId $s.Id -ChipIdReg 0x0F `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.WhoAmI.Define(this, $("0x{0:X2}" -f $s.Id));
            Registers.CtrlReg1XL.Define(this);
            Registers.CtrlReg2G.Define(this);
            Registers.StatusReg.Define(this, 0x07);
            Registers.TempOutL.Define(this).WithValueField(0,8,out tL,FieldMode.Read);
            Registers.TempOutH.Define(this).WithValueField(0,8,out tH,FieldMode.Read);
            Registers.GyroXL.Define(this).WithValueField(0,8,out gxL,FieldMode.Read);
            Registers.GyroXH.Define(this).WithValueField(0,8,out gxH,FieldMode.Read);
            Registers.GyroYL.Define(this).WithValueField(0,8,out gyL,FieldMode.Read);
            Registers.GyroYH.Define(this).WithValueField(0,8,out gyH,FieldMode.Read);
            Registers.GyroZL.Define(this).WithValueField(0,8,out gzL,FieldMode.Read);
            Registers.GyroZH.Define(this).WithValueField(0,8,out gzH,FieldMode.Read);
            Registers.AccXL.Define(this).WithValueField(0,8,out axL,FieldMode.Read);
            Registers.AccXH.Define(this).WithValueField(0,8,out axH,FieldMode.Read);
            Registers.AccYL.Define(this).WithValueField(0,8,out ayL,FieldMode.Read);
            Registers.AccYH.Define(this).WithValueField(0,8,out ayH,FieldMode.Read);
            Registers.AccZL.Define(this).WithValueField(0,8,out azL,FieldMode.Read);
            Registers.AccZH.Define(this).WithValueField(0,8,out azH,FieldMode.Read);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var ax=(short)(AccelerationX*16384m); axL.Value=(byte)(ax&0xFF); axH.Value=(byte)(ax>>8);
            var ay=(short)(AccelerationY*16384m); ayL.Value=(byte)(ay&0xFF); ayH.Value=(byte)(ay>>8);
            var az=(short)(AccelerationZ*16384m); azL.Value=(byte)(az&0xFF); azH.Value=(byte)(az>>8);
            var gx=(short)(AngularRateX*131m); gxL.Value=(byte)(gx&0xFF); gxH.Value=(byte)(gx>>8);
            var gy=(short)(AngularRateY*131m); gyL.Value=(byte)(gy&0xFF); gyH.Value=(byte)(gy>>8);
            var gz=(short)(AngularRateZ*131m); gzL.Value=(byte)(gz&0xFF); gzH.Value=(byte)(gz>>8);
            var rawT=(short)(Temperature*256m);
            tL.Value=(byte)(rawT&0xFF); tH.Value=(byte)(rawT>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField axL,axH,ayL,ayH,azL,azH,gxL,gxH,gyL,gyH,gzL,gzH,tL,tH;" `
    -RegisterEnum @"
            WhoAmI=0x0F,CtrlReg1XL=0x10,CtrlReg2G=0x11,StatusReg=0x1E,
            TempOutL=0x20,TempOutH=0x21,
            GyroXL=0x22,GyroXH=0x23,GyroYL=0x24,GyroYH=0x25,GyroZL=0x26,GyroZH=0x27,
            AccXL=0x28,AccXH=0x29,AccYL=0x2A,AccYH=0x2B,AccZL=0x2C,AccZH=0x2D,
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# ══════════════════════════════════════════════════════════════
# 20. ADXL355 – low-noise accelerometer (SPI/I2C)
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "ADXL355" `
    -Comment "Analog Devices ADXL355 - low-noise 3-axis accelerometer (I2C), Device ID=0xAD" `
    -ChipId 0xAD -ChipIdReg 0x00 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
"@ `
    -RegisterDefs @"
            Registers.DevIdAD.Define(this, 0xAD);
            Registers.DevIdMST.Define(this, 0x1D);
            Registers.PartId.Define(this, 0xED);
            Registers.Status.Define(this, 0x01);
            Registers.XData3.Define(this).WithValueField(0,8,out ax3,FieldMode.Read);
            Registers.XData2.Define(this).WithValueField(0,8,out ax2,FieldMode.Read);
            Registers.XData1.Define(this).WithValueField(0,8,out ax1,FieldMode.Read);
            Registers.YData3.Define(this).WithValueField(0,8,out ay3,FieldMode.Read);
            Registers.YData2.Define(this).WithValueField(0,8,out ay2,FieldMode.Read);
            Registers.YData1.Define(this).WithValueField(0,8,out ay1,FieldMode.Read);
            Registers.ZData3.Define(this).WithValueField(0,8,out az3,FieldMode.Read);
            Registers.ZData2.Define(this).WithValueField(0,8,out az2,FieldMode.Read);
            Registers.ZData1.Define(this).WithValueField(0,8,out az1,FieldMode.Read);
            Registers.TempH.Define(this).WithValueField(0,8,out tempH,FieldMode.Read);
            Registers.TempL.Define(this).WithValueField(0,8,out tempL,FieldMode.Read);
            Registers.PowerCtl.Define(this);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // 20-bit, +/-2g => 256000 LSB/g
            var ax=(int)(AccelerationX*256000m); ax3.Value=(byte)((ax>>12)&0xFF); ax2.Value=(byte)((ax>>4)&0xFF); ax1.Value=(byte)((ax&0xF)<<4);
            var ay=(int)(AccelerationY*256000m); ay3.Value=(byte)((ay>>12)&0xFF); ay2.Value=(byte)((ay>>4)&0xFF); ay1.Value=(byte)((ay&0xF)<<4);
            var az=(int)(AccelerationZ*256000m); az3.Value=(byte)((az>>12)&0xFF); az2.Value=(byte)((az>>4)&0xFF); az1.Value=(byte)((az&0xF)<<4);
            var rawT=(ushort)((Temperature+25m)*(-9.05m)+1852);
            tempH.Value=(byte)((rawT>>8)&0x0F); tempL.Value=(byte)(rawT&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField ax3,ax2,ax1,ay3,ay2,ay1,az3,az2,az1,tempH,tempL;" `
    -RegisterEnum @"
            DevIdAD=0x00,DevIdMST=0x01,PartId=0x02,
            Status=0x04,
            TempH=0x06,TempL=0x07,
            XData3=0x08,XData2=0x09,XData1=0x0A,
            YData3=0x0B,YData2=0x0C,YData1=0x0D,
            ZData3=0x0E,ZData2=0x0F,ZData1=0x10,
            PowerCtl=0x2D,
"@
Set-Content "$sensorsDir\ADXL355.cs" $f -Encoding UTF8

Write-Host "Batch 2 done (IMU/accel sensors)."

# ══════════════════════════════════════════════════════════════
# 21-25: Distance/proximity sensors
# ══════════════════════════════════════════════════════════════
foreach($s in @(
    @{N="VL53L0X"; Id=0xEE; IdReg=0xC0; Comment="ST VL53L0X - Time-of-Flight distance sensor (I2C)"; DefDist=200},
    @{N="VL53L1X"; Id=0xEA; IdReg=0x010F; Comment="ST VL53L1X - long-range ToF distance sensor (I2C)"; DefDist=500},
    @{N="VL6180X"; Id=0xB4; IdReg=0x00; Comment="ST VL6180X - proximity/ALS sensor (I2C)"; DefDist=100}
)) {
$f = New-I2CSensor -ClassName $s.N -Comment $s.Comment -ChipId $s.Id -ChipIdReg $s.IdReg `
    -Interfaces "" `
    -Properties @"
        public int DistanceMm { get; set; } = $($s.DefDist);
        public int AmbientLight { get; set; } = 500;
"@ `
    -RegisterDefs @"
            Registers.ModelId.Define(this, $("0x{0:X2}" -f $s.Id));
            Registers.RangeStatus.Define(this, 0x01);
            Registers.RangeValueH.Define(this).WithValueField(0,8,out rangeH,FieldMode.Read);
            Registers.RangeValueL.Define(this).WithValueField(0,8,out rangeL,FieldMode.Read);
            Registers.SystemStart.Define(this).WithWriteCallback((_,__) => UpdateReadout());
            UpdateReadout();
"@ `
    -UpdateBody @"
            rangeH.Value = (byte)((DistanceMm >> 8) & 0xFF);
            rangeL.Value = (byte)(DistanceMm & 0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField rangeH, rangeL;" `
    -RegisterEnum @"
            ModelId=0x00, RangeStatus=0x09,
            RangeValueH=0x1E, RangeValueL=0x1F,
            SystemStart=0x80,
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# APDS9960
$f = New-I2CSensor -ClassName "APDS9960" `
    -Comment "Broadcom APDS-9960 - RGB, proximity and gesture sensor (I2C), ID=0xAB" `
    -ChipId 0xAB -ChipIdReg 0x92 `
    -Interfaces "" `
    -Properties @"
        public int Proximity { get; set; } = 100;
        public int Red { get; set; } = 500;
        public int Green { get; set; } = 500;
        public int Blue { get; set; } = 500;
        public int Clear { get; set; } = 1500;
"@ `
    -RegisterDefs @"
            Registers.Enable.Define(this);
            Registers.Status.Define(this, 0x07);
            Registers.ClearL.Define(this).WithValueField(0,8,out cL,FieldMode.Read);
            Registers.ClearH.Define(this).WithValueField(0,8,out cH,FieldMode.Read);
            Registers.RedL.Define(this).WithValueField(0,8,out rL,FieldMode.Read);
            Registers.RedH.Define(this).WithValueField(0,8,out rH,FieldMode.Read);
            Registers.GreenL.Define(this).WithValueField(0,8,out gL,FieldMode.Read);
            Registers.GreenH.Define(this).WithValueField(0,8,out gH,FieldMode.Read);
            Registers.BlueL.Define(this).WithValueField(0,8,out bL,FieldMode.Read);
            Registers.BlueH.Define(this).WithValueField(0,8,out bH,FieldMode.Read);
            Registers.ProxData.Define(this).WithValueField(0,8,out proxData,FieldMode.Read);
            Registers.ID.Define(this, 0xAB);
            UpdateReadout();
"@ `
    -UpdateBody @"
            cL.Value=(byte)(Clear&0xFF); cH.Value=(byte)(Clear>>8);
            rL.Value=(byte)(Red&0xFF); rH.Value=(byte)(Red>>8);
            gL.Value=(byte)(Green&0xFF); gH.Value=(byte)(Green>>8);
            bL.Value=(byte)(Blue&0xFF); bH.Value=(byte)(Blue>>8);
            proxData.Value=(byte)(Proximity&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField cL,cH,rL,rH,gL,gH,bL,bH,proxData;" `
    -RegisterEnum @"
            Enable=0x80,Status=0x93,
            ClearL=0x94,ClearH=0x95,RedL=0x96,RedH=0x97,
            GreenL=0x98,GreenH=0x99,BlueL=0x9A,BlueH=0x9B,
            ProxData=0x9C,
            ID=0x92,
"@
Set-Content "$sensorsDir\APDS9960.cs" $f -Encoding UTF8

# VCNL4040
$f = New-I2CSensor -ClassName "VCNL4040" `
    -Comment "Vishay VCNL4040 - proximity and ambient light sensor (I2C), ID=0x0186" `
    -ChipId 0x86 -ChipIdReg 0x0C `
    -Interfaces "" `
    -Properties @"
        public int Proximity { get; set; } = 100;
        public int AmbientLight { get; set; } = 500;
"@ `
    -RegisterDefs @"
            Registers.AlsConf.Define(this);
            Registers.PsConf1.Define(this);
            Registers.PsDataL.Define(this).WithValueField(0,8,out psL,FieldMode.Read);
            Registers.PsDataH.Define(this).WithValueField(0,8,out psH,FieldMode.Read);
            Registers.AlsDataL.Define(this).WithValueField(0,8,out alsL,FieldMode.Read);
            Registers.AlsDataH.Define(this).WithValueField(0,8,out alsH,FieldMode.Read);
            Registers.DeviceIdL.Define(this, 0x86);
            Registers.DeviceIdH.Define(this, 0x01);
            UpdateReadout();
"@ `
    -UpdateBody @"
            psL.Value=(byte)(Proximity&0xFF); psH.Value=(byte)(Proximity>>8);
            alsL.Value=(byte)(AmbientLight&0xFF); alsH.Value=(byte)(AmbientLight>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField psL,psH,alsL,alsH;" `
    -RegisterEnum @"
            AlsConf=0x00,PsConf1=0x03,
            PsDataL=0x08,PsDataH=0x09,
            AlsDataL=0x0A,AlsDataH=0x0B,
            DeviceIdL=0x0C,DeviceIdH=0x0D,
"@
Set-Content "$sensorsDir\VCNL4040.cs" $f -Encoding UTF8

Write-Host "Batch 3 done (distance/proximity)."

# ══════════════════════════════════════════════════════════════
# 26-30: Light/color sensors
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "TSL2591" `
    -Comment "ams TSL2591 - high-dynamic-range light sensor (I2C), Device ID=0x50" `
    -ChipId 0x50 -ChipIdReg 0x12 `
    -Interfaces "" `
    -Properties @"
        public int FullSpectrum { get; set; } = 500;
        public int Infrared { get; set; } = 100;
"@ `
    -RegisterDefs @"
            Registers.Enable.Define(this, 0x01);
            Registers.Config.Define(this);
            Registers.Status.Define(this, 0x01);
            Registers.Ch0L.Define(this).WithValueField(0,8,out ch0L,FieldMode.Read);
            Registers.Ch0H.Define(this).WithValueField(0,8,out ch0H,FieldMode.Read);
            Registers.Ch1L.Define(this).WithValueField(0,8,out ch1L,FieldMode.Read);
            Registers.Ch1H.Define(this).WithValueField(0,8,out ch1H,FieldMode.Read);
            Registers.Id.Define(this, 0x50);
            UpdateReadout();
"@ `
    -UpdateBody @"
            ch0L.Value=(byte)(FullSpectrum&0xFF); ch0H.Value=(byte)(FullSpectrum>>8);
            ch1L.Value=(byte)(Infrared&0xFF); ch1H.Value=(byte)(Infrared>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField ch0L,ch0H,ch1L,ch1H;" `
    -RegisterEnum @"
            Enable=0x00,Config=0x01,Status=0x13,
            Ch0L=0x14,Ch0H=0x15,Ch1L=0x16,Ch1H=0x17,
            Id=0x12,
"@
Set-Content "$sensorsDir\TSL2591.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "BH1750" `
    -Comment "ROHM BH1750FVI - ambient light sensor (I2C)" `
    -ChipId 0x00 -ChipIdReg 0x00 `
    -Interfaces "" `
    -Properties "        public int LuxValue { get; set; } = 500;" `
    -RegisterDefs @"
            // BH1750 uses command-based protocol, but we'll provide data on read
            UpdateReadout();
"@ `
    -UpdateBody @"
            // Raw = lux / 1.2
"@ `
    -ReadoutFields "" `
    -RegisterEnum "            PowerOn=0x01, MeasureHRes=0x10," `
    -ExtraMethods @"

        // Override Read to return lux directly (BH1750 returns 2 bytes on read)
        public new byte[] Read(int count)
        {
            var raw = (ushort)(LuxValue / 1.2m * 1m);
            return new byte[] { (byte)(raw >> 8), (byte)(raw & 0xFF) };
        }
"@
# BH1750 actually uses a simple command+read protocol, let's handle it as cmd-based
$f = New-CmdSensor -ClassName "BH1750" `
    -Comment "ROHM BH1750FVI - ambient light sensor (I2C)" `
    -Interfaces "" `
    -Properties "        public int LuxValue { get; set; } = 500;" `
    -WriteBody @"
            // BH1750 commands: 0x01=PowerOn, 0x10=ContinuousHiRes, etc.
            if(data[0] == 0x10 || data[0] == 0x11 || data[0] == 0x20)
            {
                var raw = (ushort)(LuxValue / 1.2 * 1.0);
                readBuffer = new byte[] { (byte)(raw >> 8), (byte)(raw & 0xFF) };
                readIndex = 0;
            }
"@
Set-Content "$sensorsDir\BH1750.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "VEML7700" `
    -Comment "Vishay VEML7700 - ambient light sensor (I2C)" `
    -ChipId 0x00 -ChipIdReg 0x00 `
    -Interfaces "" `
    -Properties "        public int LuxValue { get; set; } = 500;" `
    -RegisterDefs @"
            Registers.AlsConf.Define(this);
            Registers.AlsWH.Define(this);
            Registers.AlsWL.Define(this);
            Registers.PowerSave.Define(this);
            Registers.AlsL.Define(this).WithValueField(0,8,out alsL,FieldMode.Read);
            Registers.AlsH.Define(this).WithValueField(0,8,out alsH,FieldMode.Read);
            Registers.WhiteL.Define(this).WithValueField(0,8,out whiteL,FieldMode.Read);
            Registers.WhiteH.Define(this).WithValueField(0,8,out whiteH,FieldMode.Read);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var raw = (ushort)(LuxValue / 0.0576);
            alsL.Value=(byte)(raw&0xFF); alsH.Value=(byte)(raw>>8);
            whiteL.Value=(byte)(raw&0xFF); whiteH.Value=(byte)(raw>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField alsL,alsH,whiteL,whiteH;" `
    -RegisterEnum @"
            AlsConf=0x00,AlsWH=0x01,AlsWL=0x02,PowerSave=0x03,
            AlsL=0x04,AlsH=0x05,WhiteL=0x06,WhiteH=0x07,
"@
Set-Content "$sensorsDir\VEML7700.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "TCS34725" `
    -Comment "ams TCS34725 - color sensor (I2C), Device ID=0x44" `
    -ChipId 0x44 -ChipIdReg 0x12 `
    -Interfaces "" `
    -Properties @"
        public int Red { get; set; } = 500;
        public int Green { get; set; } = 500;
        public int Blue { get; set; } = 500;
        public int Clear { get; set; } = 1500;
"@ `
    -RegisterDefs @"
            Registers.Enable.Define(this, 0x01);
            Registers.ATime.Define(this, 0xFF);
            Registers.Control.Define(this, 0x01);
            Registers.Id.Define(this, 0x44);
            Registers.Status.Define(this, 0x01);
            Registers.ClearL.Define(this).WithValueField(0,8,out cL,FieldMode.Read);
            Registers.ClearH.Define(this).WithValueField(0,8,out cH,FieldMode.Read);
            Registers.RedL.Define(this).WithValueField(0,8,out rL,FieldMode.Read);
            Registers.RedH.Define(this).WithValueField(0,8,out rH,FieldMode.Read);
            Registers.GreenL.Define(this).WithValueField(0,8,out gL,FieldMode.Read);
            Registers.GreenH.Define(this).WithValueField(0,8,out gH,FieldMode.Read);
            Registers.BlueL.Define(this).WithValueField(0,8,out bL,FieldMode.Read);
            Registers.BlueH.Define(this).WithValueField(0,8,out bH,FieldMode.Read);
            UpdateReadout();
"@ `
    -UpdateBody @"
            cL.Value=(byte)(Clear&0xFF);cH.Value=(byte)(Clear>>8);
            rL.Value=(byte)(Red&0xFF);rH.Value=(byte)(Red>>8);
            gL.Value=(byte)(Green&0xFF);gH.Value=(byte)(Green>>8);
            bL.Value=(byte)(Blue&0xFF);bH.Value=(byte)(Blue>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField cL,cH,rL,rH,gL,gH,bL,bH;" `
    -RegisterEnum @"
            Enable=0x80,ATime=0x81,Control=0x8F,Id=0x92,Status=0x93,
            ClearL=0x94,ClearH=0x95,RedL=0x96,RedH=0x97,
            GreenL=0x98,GreenH=0x99,BlueL=0x9A,BlueH=0x9B,
"@
Set-Content "$sensorsDir\TCS34725.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "OPT3001" `
    -Comment "TI OPT3001 - ambient light sensor (I2C), Manufacturer ID=0x5449" `
    -ChipId 0x49 -ChipIdReg 0x7F `
    -Interfaces "" `
    -Properties "        public int LuxValue { get; set; } = 500;" `
    -RegisterDefs @"
            Registers.ResultMSB.Define(this).WithValueField(0,8,out resH,FieldMode.Read);
            Registers.ResultLSB.Define(this).WithValueField(0,8,out resL,FieldMode.Read);
            Registers.Configuration.Define(this, 0xC8);
            Registers.ManufacturerIdH.Define(this, 0x54);
            Registers.ManufacturerIdL.Define(this, 0x49);
            Registers.DeviceIdH.Define(this, 0x30);
            Registers.DeviceIdL.Define(this, 0x01);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // 12-bit mantissa, 4-bit exponent: result = mantissa << exponent * 0.01 lux
            var val = (ushort)LuxValue;
            var exp = 0; var mant = val * 100;
            while(mant > 0xFFF && exp < 11) { mant >>= 1; exp++; }
            resH.Value=(byte)(((exp&0xF)<<4)|((mant>>8)&0xF));
            resL.Value=(byte)(mant&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField resH,resL;" `
    -RegisterEnum @"
            ResultMSB=0x00,ResultLSB=0x01,
            Configuration=0x02,
            ManufacturerIdH=0x7E,ManufacturerIdL=0x7F,
            DeviceIdH=0x80,DeviceIdL=0x81,
"@
Set-Content "$sensorsDir\OPT3001.cs" $f -Encoding UTF8

Write-Host "Batch 4 done (light/color sensors)."

# ══════════════════════════════════════════════════════════════
# 31-35: Gas/Air quality sensors
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "CCS811" `
    -Comment "ams CCS811 - indoor air quality sensor (I2C), HW_ID=0x81" `
    -ChipId 0x81 -ChipIdReg 0x20 `
    -Interfaces "" `
    -Properties @"
        public int eCO2 { get; set; } = 400;
        public int TVOC { get; set; } = 0;
"@ `
    -RegisterDefs @"
            Registers.Status.Define(this, 0x98);
            Registers.MeasMode.Define(this);
            Registers.AlgResultECO2H.Define(this).WithValueField(0,8,out eco2H,FieldMode.Read);
            Registers.AlgResultECO2L.Define(this).WithValueField(0,8,out eco2L,FieldMode.Read);
            Registers.AlgResultTVOCH.Define(this).WithValueField(0,8,out tvocH,FieldMode.Read);
            Registers.AlgResultTVOCL.Define(this).WithValueField(0,8,out tvocL,FieldMode.Read);
            Registers.HwId.Define(this, 0x81);
            Registers.HwVersion.Define(this, 0x1A);
            Registers.SwReset.Define(this).WithWriteCallback((_,__) => Reset());
            UpdateReadout();
"@ `
    -UpdateBody @"
            eco2H.Value=(byte)((eCO2>>8)&0xFF); eco2L.Value=(byte)(eCO2&0xFF);
            tvocH.Value=(byte)((TVOC>>8)&0xFF); tvocL.Value=(byte)(TVOC&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField eco2H,eco2L,tvocH,tvocL;" `
    -RegisterEnum @"
            Status=0x00,MeasMode=0x01,
            AlgResultECO2H=0x02,AlgResultECO2L=0x03,AlgResultTVOCH=0x04,AlgResultTVOCL=0x05,
            HwId=0x20,HwVersion=0x21,
            SwReset=0xFF,
"@
Set-Content "$sensorsDir\CCS811.cs" $f -Encoding UTF8

$f = New-CmdSensor -ClassName "SGP30" `
    -Comment "Sensirion SGP30 - TVOC and eCO2 sensor (I2C)" `
    -Interfaces "" `
    -Properties @"
        public int eCO2 { get; set; } = 400;
        public int TVOC { get; set; } = 0;
"@ `
    -WriteBody @"
            if(data.Length < 2) return;
            var cmd = (ushort)((data[0] << 8) | data[1]);
            switch(cmd)
            {
                case 0x2008: // Measure air quality
                    readBuffer = new byte[]
                    {
                        (byte)((eCO2 >> 8) & 0xFF), (byte)(eCO2 & 0xFF), 0x00,
                        (byte)((TVOC >> 8) & 0xFF), (byte)(TVOC & 0xFF), 0x00,
                    };
                    readIndex = 0;
                    break;
                case 0x2003: // Init air quality
                case 0x0020: // Get serial
                    readBuffer = new byte[] { 0x00, 0x01, 0x00, 0x02, 0x03, 0x00, 0x04, 0x05, 0x00 };
                    readIndex = 0;
                    break;
                case 0x2050: // Measure test
                    readBuffer = new byte[] { 0xD4, 0x00, 0x00 };
                    readIndex = 0;
                    break;
            }
"@
Set-Content "$sensorsDir\SGP30.cs" $f -Encoding UTF8

$f = New-CmdSensor -ClassName "SGP40" `
    -Comment "Sensirion SGP40 - VOC index sensor (I2C)" `
    -Interfaces "" `
    -Properties "        public int VocIndex { get; set; } = 100;" `
    -WriteBody @"
            if(data.Length < 2) return;
            var cmd = (ushort)((data[0] << 8) | data[1]);
            if(cmd == 0x260F) // Measure raw signal
            {
                var raw = (ushort)VocIndex;
                readBuffer = new byte[] { (byte)(raw >> 8), (byte)(raw & 0xFF), 0x00 };
                readIndex = 0;
            }
            else if(cmd == 0x3682) // Get serial
            {
                readBuffer = new byte[] { 0x00, 0x01, 0x00, 0x02, 0x03, 0x00, 0x04, 0x05, 0x00 };
                readIndex = 0;
            }
"@
Set-Content "$sensorsDir\SGP40.cs" $f -Encoding UTF8

$f = New-CmdSensor -ClassName "SCD40" `
    -Comment "Sensirion SCD40 - CO2, humidity, temperature sensor (I2C)" `
    -Interfaces "ITemperatureSensor, IHumiditySensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Humidity { get; set; } = 50.0m;
        public int CO2 { get; set; } = 400;
"@ `
    -WriteBody @"
            if(data.Length < 2) return;
            var cmd = (ushort)((data[0] << 8) | data[1]);
            switch(cmd)
            {
                case 0xEC05: // Read measurement
                    var rawCO2 = (ushort)CO2;
                    var rawT = (ushort)((Temperature + 45m) * 65536m / 175m);
                    var rawH = (ushort)(Humidity * 65536m / 100m);
                    readBuffer = new byte[]
                    {
                        (byte)(rawCO2 >> 8), (byte)(rawCO2 & 0xFF), 0x00,
                        (byte)(rawT >> 8), (byte)(rawT & 0xFF), 0x00,
                        (byte)(rawH >> 8), (byte)(rawH & 0xFF), 0x00,
                    };
                    readIndex = 0;
                    break;
                case 0x21B1: // Start periodic measurement
                case 0x3F86: // Stop
                case 0xE4B8: // Get data ready
                    readBuffer = new byte[] { 0x00, 0x01, 0x00 }; // data ready
                    readIndex = 0;
                    break;
                case 0x3646: // Get serial
                    readBuffer = new byte[] { 0xBE, 0xEF, 0x00, 0x12, 0x34, 0x00, 0x56, 0x78, 0x00 };
                    readIndex = 0;
                    break;
            }
"@
Set-Content "$sensorsDir\SCD40.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "ENS160" `
    -Comment "ScioSense ENS160 - digital metal oxide multi-gas sensor (I2C), Part ID=0x0160" `
    -ChipId 0x60 -ChipIdReg 0x00 `
    -Interfaces "" `
    -Properties @"
        public int eCO2 { get; set; } = 400;
        public int TVOC { get; set; } = 0;
        public int AQI { get; set; } = 1;
"@ `
    -RegisterDefs @"
            Registers.PartIdL.Define(this, 0x60);
            Registers.PartIdH.Define(this, 0x01);
            Registers.OpMode.Define(this);
            Registers.DeviceStatus.Define(this, 0x80);
            Registers.DataAQI.Define(this).WithValueField(0,8,out aqiReg,FieldMode.Read);
            Registers.DataTVOCL.Define(this).WithValueField(0,8,out tvocL,FieldMode.Read);
            Registers.DataTVOCH.Define(this).WithValueField(0,8,out tvocH,FieldMode.Read);
            Registers.DataECO2L.Define(this).WithValueField(0,8,out eco2L,FieldMode.Read);
            Registers.DataECO2H.Define(this).WithValueField(0,8,out eco2H,FieldMode.Read);
            UpdateReadout();
"@ `
    -UpdateBody @"
            aqiReg.Value=(byte)AQI;
            tvocL.Value=(byte)(TVOC&0xFF); tvocH.Value=(byte)(TVOC>>8);
            eco2L.Value=(byte)(eCO2&0xFF); eco2H.Value=(byte)(eCO2>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField aqiReg,tvocL,tvocH,eco2L,eco2H;" `
    -RegisterEnum @"
            PartIdL=0x00,PartIdH=0x01,OpMode=0x10,
            DeviceStatus=0x20,DataAQI=0x21,
            DataTVOCL=0x22,DataTVOCH=0x23,
            DataECO2L=0x24,DataECO2H=0x25,
"@
Set-Content "$sensorsDir\ENS160.cs" $f -Encoding UTF8

Write-Host "Batch 5 done (gas/air quality)."

# ══════════════════════════════════════════════════════════════
# 36-40: Current/power/voltage sensors
# ══════════════════════════════════════════════════════════════
foreach($s in @(
    @{N="INA219"; Id=0x39; Comment="TI INA219 - current/power monitor (I2C)"; DefaultBusV=12},
    @{N="INA226"; Id=0x26; Comment="TI INA226 - current/power monitor (I2C)"; DefaultBusV=12},
    @{N="INA260"; Id=0x27; Comment="TI INA260 - current/power monitor (I2C)"; DefaultBusV=12}
)) {
$f = New-I2CSensor -ClassName $s.N -Comment $s.Comment -ChipId $s.Id -ChipIdReg 0xFF `
    -Interfaces "" `
    -Properties @"
        public decimal BusVoltage { get; set; } = $($s.DefaultBusV).0m;
        public decimal Current { get; set; } = 0.5m;
        public decimal Power { get; set; } = 6.0m;
"@ `
    -RegisterDefs @"
            Registers.ConfigMSB.Define(this, 0x39);
            Registers.ConfigLSB.Define(this, 0x9F);
            Registers.ShuntVoltageMSB.Define(this).WithValueField(0,8,out svH,FieldMode.Read);
            Registers.ShuntVoltageLSB.Define(this).WithValueField(0,8,out svL,FieldMode.Read);
            Registers.BusVoltageMSB.Define(this).WithValueField(0,8,out bvH,FieldMode.Read);
            Registers.BusVoltageLSB.Define(this).WithValueField(0,8,out bvL,FieldMode.Read);
            Registers.PowerMSB.Define(this).WithValueField(0,8,out pwH,FieldMode.Read);
            Registers.PowerLSB.Define(this).WithValueField(0,8,out pwL,FieldMode.Read);
            Registers.CurrentMSB.Define(this).WithValueField(0,8,out crH,FieldMode.Read);
            Registers.CurrentLSB.Define(this).WithValueField(0,8,out crL,FieldMode.Read);
            Registers.ManufacturerIdH.Define(this, 0x54);
            Registers.ManufacturerIdL.Define(this, 0x49);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // Bus voltage: 4mV/LSB, shifted left 3 for INA219
            var rawBV = (short)(BusVoltage / 0.004m);
            bvH.Value=(byte)(rawBV>>8); bvL.Value=(byte)(rawBV&0xFF);
            // Shunt voltage: 10uV/LSB
            var rawSV = (short)(Current * 0.1m / 0.00001m); // assuming 0.1 ohm shunt
            svH.Value=(byte)(rawSV>>8); svL.Value=(byte)(rawSV&0xFF);
            // Current: 1mA/LSB
            var rawCur = (short)(Current * 1000m);
            crH.Value=(byte)(rawCur>>8); crL.Value=(byte)(rawCur&0xFF);
            // Power: 20mW/LSB
            var rawPow = (short)(Power / 0.02m);
            pwH.Value=(byte)(rawPow>>8); pwL.Value=(byte)(rawPow&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField svH,svL,bvH,bvL,pwH,pwL,crH,crL;" `
    -RegisterEnum @"
            ConfigMSB=0x00,ConfigLSB=0x01,
            ShuntVoltageMSB=0x02,ShuntVoltageLSB=0x03,
            BusVoltageMSB=0x04,BusVoltageLSB=0x05,
            PowerMSB=0x06,PowerLSB=0x07,
            CurrentMSB=0x08,CurrentLSB=0x09,
            ManufacturerIdH=0xFE,ManufacturerIdL=0xFF,
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# ADS1115 - 16-bit ADC
$f = New-I2CSensor -ClassName "ADS1115" `
    -Comment "TI ADS1115 - 16-bit ADC, 4-channel (I2C)" `
    -ChipId 0x00 -ChipIdReg 0x00 `
    -Interfaces "" `
    -Properties @"
        public decimal Channel0Voltage { get; set; } = 1.5m;
        public decimal Channel1Voltage { get; set; } = 1.0m;
        public decimal Channel2Voltage { get; set; } = 0.5m;
        public decimal Channel3Voltage { get; set; } = 0.0m;
"@ `
    -RegisterDefs @"
            Registers.ConversionMSB.Define(this).WithValueField(0,8,out convH,FieldMode.Read);
            Registers.ConversionLSB.Define(this).WithValueField(0,8,out convL,FieldMode.Read);
            Registers.ConfigMSB.Define(this, 0x85).WithValueField(0,8,name:""config_hi"")
                .WithWriteCallback((_,val) => { currentMux = (byte)((val>>4)&0x07); UpdateReadout(); });
            Registers.ConfigLSB.Define(this, 0x83);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // PGA=+/-4.096V default => 1 LSB = 0.125 mV
            decimal voltage;
            switch(currentMux)
            {
                case 4: voltage = Channel0Voltage; break;
                case 5: voltage = Channel1Voltage; break;
                case 6: voltage = Channel2Voltage; break;
                case 7: voltage = Channel3Voltage; break;
                default: voltage = Channel0Voltage - Channel1Voltage; break;
            }
            var raw = (short)(voltage / 0.000125m);
            convH.Value=(byte)(raw>>8); convL.Value=(byte)(raw&0xFF);
"@ `
    -ReadoutFields @"
        private IValueRegisterField convH, convL;
        private byte currentMux;
"@ `
    -RegisterEnum @"
            ConversionMSB=0x00,ConversionLSB=0x01,
            ConfigMSB=0x02,ConfigLSB=0x03,
"@
Set-Content "$sensorsDir\ADS1115.cs" $f -Encoding UTF8

# MAX17048 - Battery fuel gauge
$f = New-I2CSensor -ClassName "MAX17048" `
    -Comment "Maxim MAX17048 - Li+ fuel gauge (I2C)" `
    -ChipId 0x08 -ChipIdReg 0x08 `
    -Interfaces "" `
    -Properties @"
        public decimal CellVoltage { get; set; } = 3.7m;
        public decimal StateOfCharge { get; set; } = 75.0m;
"@ `
    -RegisterDefs @"
            Registers.VCellMSB.Define(this).WithValueField(0,8,out vcH,FieldMode.Read);
            Registers.VCellLSB.Define(this).WithValueField(0,8,out vcL,FieldMode.Read);
            Registers.SOCMSB.Define(this).WithValueField(0,8,out socH,FieldMode.Read);
            Registers.SOCLSB.Define(this).WithValueField(0,8,out socL,FieldMode.Read);
            Registers.VersionMSB.Define(this, 0x00);
            Registers.VersionLSB.Define(this, 0x11);
            Registers.ConfigMSB.Define(this, 0x97);
            Registers.ConfigLSB.Define(this, 0x1C);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // VCELL: 78.125 uV/LSB, 16-bit
            var rawV = (ushort)(CellVoltage / 0.000078125m);
            vcH.Value=(byte)(rawV>>8); vcL.Value=(byte)(rawV&0xFF);
            // SOC: 1%/256 LSB
            var rawSOC = (ushort)(StateOfCharge * 256m);
            socH.Value=(byte)(rawSOC>>8); socL.Value=(byte)(rawSOC&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField vcH,vcL,socH,socL;" `
    -RegisterEnum @"
            VCellMSB=0x02,VCellLSB=0x03,
            SOCMSB=0x04,SOCLSB=0x05,
            VersionMSB=0x08,VersionLSB=0x09,
            ConfigMSB=0x0C,ConfigLSB=0x0D,
"@
Set-Content "$sensorsDir\MAX17048.cs" $f -Encoding UTF8

Write-Host "Batch 6 done (power/voltage)."

# ══════════════════════════════════════════════════════════════
# 41-43: Magnetometers
# ══════════════════════════════════════════════════════════════
$f = New-I2CSensor -ClassName "HMC5883L" `
    -Comment "Honeywell HMC5883L - 3-axis magnetometer (I2C)" `
    -ChipId 0x48 -ChipIdReg 0x0A `
    -Interfaces "IMagneticSensor" `
    -Properties @"
        public int MagneticFluxDensityX { get; set; }
        public int MagneticFluxDensityY { get; set; }
        public int MagneticFluxDensityZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.ConfigA.Define(this, 0x10);
            Registers.ConfigB.Define(this, 0x20);
            Registers.Mode.Define(this, 0x01);
            Registers.DataXH.Define(this).WithValueField(0,8,out xH,FieldMode.Read);
            Registers.DataXL.Define(this).WithValueField(0,8,out xL,FieldMode.Read);
            Registers.DataZH.Define(this).WithValueField(0,8,out zH,FieldMode.Read);
            Registers.DataZL.Define(this).WithValueField(0,8,out zL,FieldMode.Read);
            Registers.DataYH.Define(this).WithValueField(0,8,out yH,FieldMode.Read);
            Registers.DataYL.Define(this).WithValueField(0,8,out yL,FieldMode.Read);
            Registers.Status.Define(this, 0x01);
            Registers.IdA.Define(this, 0x48);
            Registers.IdB.Define(this, 0x34);
            Registers.IdC.Define(this, 0x33);
            UpdateReadout();
"@ `
    -UpdateBody @"
            // 1090 LSB/Gauss default, nT to Gauss: 1 Gauss = 100000 nT
            var mx = (short)(MagneticFluxDensityX / 100000.0 * 1090);
            var my = (short)(MagneticFluxDensityY / 100000.0 * 1090);
            var mz = (short)(MagneticFluxDensityZ / 100000.0 * 1090);
            xH.Value=(byte)(mx>>8); xL.Value=(byte)(mx&0xFF);
            yH.Value=(byte)(my>>8); yL.Value=(byte)(my&0xFF);
            zH.Value=(byte)(mz>>8); zL.Value=(byte)(mz&0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField xH,xL,yH,yL,zH,zL;" `
    -RegisterEnum @"
            ConfigA=0x00,ConfigB=0x01,Mode=0x02,
            DataXH=0x03,DataXL=0x04,DataZH=0x05,DataZL=0x06,DataYH=0x07,DataYL=0x08,
            Status=0x09,IdA=0x0A,IdB=0x0B,IdC=0x0C,
"@
Set-Content "$sensorsDir\HMC5883L.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "QMC5883L" `
    -Comment "QST QMC5883L - 3-axis magnetometer (I2C), Chip ID=0xFF" `
    -ChipId 0xFF -ChipIdReg 0x0D `
    -Interfaces "IMagneticSensor" `
    -Properties @"
        public int MagneticFluxDensityX { get; set; }
        public int MagneticFluxDensityY { get; set; }
        public int MagneticFluxDensityZ { get; set; }
"@ `
    -RegisterDefs @"
            Registers.DataXL.Define(this).WithValueField(0,8,out xL,FieldMode.Read);
            Registers.DataXH.Define(this).WithValueField(0,8,out xH,FieldMode.Read);
            Registers.DataYL.Define(this).WithValueField(0,8,out yL,FieldMode.Read);
            Registers.DataYH.Define(this).WithValueField(0,8,out yH,FieldMode.Read);
            Registers.DataZL.Define(this).WithValueField(0,8,out zL,FieldMode.Read);
            Registers.DataZH.Define(this).WithValueField(0,8,out zH,FieldMode.Read);
            Registers.Status.Define(this, 0x01);
            Registers.Control1.Define(this);
            Registers.Control2.Define(this);
            Registers.ChipId.Define(this, 0xFF);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var mx = (short)(MagneticFluxDensityX / 100000.0 * 3000);
            var my = (short)(MagneticFluxDensityY / 100000.0 * 3000);
            var mz = (short)(MagneticFluxDensityZ / 100000.0 * 3000);
            xL.Value=(byte)(mx&0xFF); xH.Value=(byte)(mx>>8);
            yL.Value=(byte)(my&0xFF); yH.Value=(byte)(my>>8);
            zL.Value=(byte)(mz&0xFF); zH.Value=(byte)(mz>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField xL,xH,yL,yH,zL,zH;" `
    -RegisterEnum @"
            DataXL=0x00,DataXH=0x01,DataYL=0x02,DataYH=0x03,
            DataZL=0x04,DataZH=0x05,Status=0x06,
            Control1=0x09,Control2=0x0A,ChipId=0x0D,
"@
Set-Content "$sensorsDir\QMC5883L.cs" $f -Encoding UTF8

$f = New-I2CSensor -ClassName "MLX90393" `
    -Comment "Melexis MLX90393 - 3-axis magnetometer (I2C)" `
    -ChipId 0x00 -ChipIdReg 0x00 `
    -Interfaces "IMagneticSensor" `
    -Properties @"
        public int MagneticFluxDensityX { get; set; }
        public int MagneticFluxDensityY { get; set; }
        public int MagneticFluxDensityZ { get; set; }
"@ `
    -RegisterDefs @"
            // MLX90393 uses command words, but we'll provide register-like access
            UpdateReadout();
"@ `
    -UpdateBody @"
"@ `
    -ReadoutFields "" `
    -RegisterEnum "            Status=0x00," `
    -ExtraMethods @"

        // MLX90393 uses command-based protocol
        public new byte[] Read(int count)
        {
            // Return status + X/Y/Z/T data
            var mx = (short)(MagneticFluxDensityX / 100000.0 * 3000);
            var my = (short)(MagneticFluxDensityY / 100000.0 * 3000);
            var mz = (short)(MagneticFluxDensityZ / 100000.0 * 3000);
            return new byte[] { 0x00, (byte)(mx>>8),(byte)(mx&0xFF), (byte)(my>>8),(byte)(my&0xFF), (byte)(mz>>8),(byte)(mz&0xFF) };
        }
"@
# MLX90393 is really command-based. Let's use the cmd pattern:
$f = New-CmdSensor -ClassName "MLX90393" `
    -Comment "Melexis MLX90393 - 3-axis magnetometer (I2C)" `
    -Interfaces "IMagneticSensor" `
    -Properties @"
        public int MagneticFluxDensityX { get; set; }
        public int MagneticFluxDensityY { get; set; }
        public int MagneticFluxDensityZ { get; set; }
"@ `
    -WriteBody @"
            var cmd = data[0];
            if((cmd & 0xF0) == 0x30) // Single measurement
            {
                readBuffer = new byte[] { 0x01 }; // status
                readIndex = 0;
            }
            else if((cmd & 0xF0) == 0x40) // Read measurement
            {
                var mx = (short)(MagneticFluxDensityX / 100000.0 * 3000);
                var my = (short)(MagneticFluxDensityY / 100000.0 * 3000);
                var mz = (short)(MagneticFluxDensityZ / 100000.0 * 3000);
                readBuffer = new byte[] { 0x01, (byte)(mx>>8),(byte)(mx&0xFF), (byte)(my>>8),(byte)(my&0xFF), (byte)(mz>>8),(byte)(mz&0xFF) };
                readIndex = 0;
            }
            else if(cmd == 0x80) // Reset
            { Reset(); readBuffer = new byte[]{0x01}; readIndex=0; }
"@
Set-Content "$sensorsDir\MLX90393.cs" $f -Encoding UTF8

Write-Host "Batch 7 done (magnetometers)."

# ══════════════════════════════════════════════════════════════
# 44-45: SPI thermocouple sensors
# ══════════════════════════════════════════════════════════════
$f = New-SPISensor -ClassName "MAX31855" `
    -Comment "Maxim MAX31855 - thermocouple-to-digital converter (SPI)" `
    -Interfaces "ITemperatureSensor" `
    -Properties "        public decimal Temperature { get; set; } = 25.0m;" `
    -TransmitBody @"
            // 32-bit read: bits [31:18] = 14-bit thermocouple temp (0.25C/LSB)
            // bits [15:4] = 12-bit internal temp (0.0625C/LSB)
            var tcRaw = (int)(Temperature * 4m);
            var intRaw = (int)(25m * 16m);
            uint word = ((uint)(tcRaw & 0x3FFF) << 18) | ((uint)(intRaw & 0xFFF) << 4);
            byte value = (byte)((word >> (24 - byteIndex * 8)) & 0xFF);
            byteIndex++;
            return value;
"@ `
    -Fields ""
Set-Content "$sensorsDir\MAX31855.cs" $f -Encoding UTF8

$f = New-SPISensor -ClassName "MAX31856" `
    -Comment "Maxim MAX31856 - precision thermocouple-to-digital (SPI)" `
    -Interfaces "ITemperatureSensor" `
    -Properties "        public decimal Temperature { get; set; } = 25.0m;" `
    -TransmitBody @"
            // Register-based SPI: first byte = address (bit 7: 0=read, 1=write)
            if(byteIndex == 0) { currentRegister = (byte)(data & 0x7F); isWrite = (data & 0x80) != 0; byteIndex++; return 0; }
            if(isWrite) { registers[currentRegister] = data; byteIndex++; return 0; }
            byte result = 0;
            switch(currentRegister + byteIndex - 1)
            {
                case 0x0C: result = (byte)((int)(Temperature * 16m) >> 11); break;
                case 0x0D: result = (byte)((int)(Temperature * 16m) >> 3); break;
                case 0x0E: result = (byte)(((int)(Temperature * 16m) & 0x07) << 5); break;
            }
            byteIndex++;
            return result;
"@ `
    -Fields @"
        private byte currentRegister;
        private bool isWrite;
        private byte[] registers = new byte[0x20];
"@
Set-Content "$sensorsDir\MAX31856.cs" $f -Encoding UTF8

Write-Host "Batch 8 done (SPI thermocouple)."

# ══════════════════════════════════════════════════════════════
# 46-50: Additional popular sensors
# ══════════════════════════════════════════════════════════════

# BNO085 - simplified SH-2 protocol
$f = New-CmdSensor -ClassName "BNO085" `
    -Comment "CEVA BNO085 - 9-axis sensor fusion (I2C/SPI), SH-2 protocol" `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
        public decimal AngularRateX { get; set; }
        public decimal AngularRateY { get; set; }
        public decimal AngularRateZ { get; set; }
"@ `
    -WriteBody @"
            // Simplified SH-2: respond with product ID report to any command
            readBuffer = new byte[]
            {
                0x14, 0x00, // length
                0x01, 0x01, // channel, sequence
                0xF8, 0x00, // product ID response
                0x01, // reset cause
                0x04, 0x03, 0x02, 0x01, // SW version
                0x00, 0x00, 0x00, 0x00, // part number
                0x00, 0x00, 0x00, 0x00, // build
                0x00, 0x00, // patch
            };
            readIndex = 0;
"@
Set-Content "$sensorsDir\BNO085.cs" $f -Encoding UTF8

# MAX30102 - pulse oximeter
$f = New-I2CSensor -ClassName "MAX30102" `
    -Comment "Maxim MAX30102 - pulse oximeter and heart-rate sensor (I2C), Part ID=0x15" `
    -ChipId 0x15 -ChipIdReg 0xFF `
    -Interfaces "" `
    -Properties @"
        public int HeartRate { get; set; } = 72;
        public int SpO2 { get; set; } = 98;
"@ `
    -RegisterDefs @"
            Registers.IntStatus1.Define(this, 0x40);
            Registers.IntStatus2.Define(this);
            Registers.FifoWrPtr.Define(this);
            Registers.OvfCounter.Define(this);
            Registers.FifoRdPtr.Define(this);
            Registers.FifoData.Define(this).WithValueField(0,8,out fifoData,FieldMode.Read);
            Registers.ModeConfig.Define(this).WithWriteCallback((_,val) => { if((val&0x40)!=0) Reset(); });
            Registers.SpO2Config.Define(this);
            Registers.RevId.Define(this, 0x06);
            Registers.PartId.Define(this, 0x15);
            UpdateReadout();
"@ `
    -UpdateBody @"
            fifoData.Value = (byte)(SpO2 & 0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField fifoData;" `
    -RegisterEnum @"
            IntStatus1=0x00,IntStatus2=0x01,
            FifoWrPtr=0x04,OvfCounter=0x05,FifoRdPtr=0x06,FifoData=0x07,
            ModeConfig=0x09,SpO2Config=0x0A,
            RevId=0xFE,PartId=0xFF,
"@
Set-Content "$sensorsDir\MAX30102.cs" $f -Encoding UTF8

# MLX90614 - IR contactless thermometer
$f = New-CmdSensor -ClassName "MLX90614" `
    -Comment "Melexis MLX90614 - infrared contactless thermometer (I2C/SMBus)" `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AmbientTemperature { get; set; } = 22.0m;
"@ `
    -WriteBody @"
            switch(data[0])
            {
                case 0x06: // Read ambient temp (Ta)
                    var rawTa = (ushort)((AmbientTemperature + 273.15m) * 50m);
                    readBuffer = new byte[] { (byte)(rawTa & 0xFF), (byte)(rawTa >> 8), 0x00 };
                    readIndex = 0;
                    break;
                case 0x07: // Read object temp (Tobj1)
                    var rawTo = (ushort)((Temperature + 273.15m) * 50m);
                    readBuffer = new byte[] { (byte)(rawTo & 0xFF), (byte)(rawTo >> 8), 0x00 };
                    readIndex = 0;
                    break;
            }
"@
Set-Content "$sensorsDir\MLX90614.cs" $f -Encoding UTF8

# AMG8833 - IR thermal camera
$f = New-I2CSensor -ClassName "AMG8833" `
    -Comment "Panasonic AMG8833 (Grid-EYE) - 8x8 infrared thermal camera (I2C)" `
    -ChipId 0x00 -ChipIdReg 0x00 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal PixelUniformTemp { get; set; } = 25.0m;
"@ `
    -RegisterDefs @"
            Registers.PowerControl.Define(this);
            Registers.Reset.Define(this).WithWriteCallback((_,val) => { if(val==0x3F) Reset(); });
            Registers.FrameRate.Define(this, 0x01);
            Registers.StatusReg.Define(this);
            Registers.ThermistorL.Define(this).WithValueField(0,8,out thermL,FieldMode.Read);
            Registers.ThermistorH.Define(this).WithValueField(0,8,out thermH,FieldMode.Read);
            // Pixel data starts at 0x80, 128 bytes for 64 pixels (16-bit each)
            for(byte addr = 0x80; addr < 0x80 + 128 && addr != 0; addr++)
            {
                ((Registers)addr).Define(this, 0x00);
            }
            UpdateReadout();
"@ `
    -UpdateBody @"
            var rawT = (short)(Temperature * 16m);
            thermL.Value = (byte)(rawT & 0xFF);
            thermH.Value = (byte)((rawT >> 8) & 0x0F);
            // Fill pixel data with uniform temperature
            var rawPix = (short)(PixelUniformTemp * 4m);
            for(byte addr = 0x80; addr < 0x80 + 128; addr += 2)
            {
                RegistersCollection.Write(addr, (byte)(rawPix & 0xFF));
                RegistersCollection.Write((byte)(addr + 1), (byte)((rawPix >> 8) & 0x0F));
            }
"@ `
    -ReadoutFields "        private IValueRegisterField thermL, thermH;" `
    -RegisterEnum @"
            PowerControl=0x00,Reset=0x01,FrameRate=0x02,
            StatusReg=0x04,
            ThermistorL=0x0E,ThermistorH=0x0F,
"@
Set-Content "$sensorsDir\AMG8833.cs" $f -Encoding UTF8

# LPS25HB
$f = New-I2CSensor -ClassName "LPS25HB" `
    -Comment "ST LPS25HB - MEMS pressure sensor (I2C), WHO_AM_I=0xBD" `
    -ChipId 0xBD -ChipIdReg 0x0F `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Pressure { get; set; } = 1013.25m;
"@ `
    -RegisterDefs @"
            Registers.WhoAmI.Define(this, 0xBD);
            Registers.CtrlReg1.Define(this);
            Registers.CtrlReg2.Define(this).WithWriteCallback((_,val) => { if((val&0x01)!=0) UpdateReadout(); });
            Registers.StatusReg.Define(this, 0x03);
            Registers.PressOutXL.Define(this).WithValueField(0,8,out pXL,FieldMode.Read);
            Registers.PressOutL.Define(this).WithValueField(0,8,out pL,FieldMode.Read);
            Registers.PressOutH.Define(this).WithValueField(0,8,out pH,FieldMode.Read);
            Registers.TempOutL.Define(this).WithValueField(0,8,out tL,FieldMode.Read);
            Registers.TempOutH.Define(this).WithValueField(0,8,out tH,FieldMode.Read);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var rawP=(int)(Pressure*4096m); pXL.Value=(byte)(rawP&0xFF); pL.Value=(byte)((rawP>>8)&0xFF); pH.Value=(byte)((rawP>>16)&0xFF);
            var rawT=(short)(Temperature*480m+42.5m*480m); tL.Value=(byte)(rawT&0xFF); tH.Value=(byte)(rawT>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField pXL,pL,pH,tL,tH;" `
    -RegisterEnum @"
            WhoAmI=0x0F,CtrlReg1=0x20,CtrlReg2=0x21,StatusReg=0x27,
            PressOutXL=0x28,PressOutL=0x29,PressOutH=0x2A,
            TempOutL=0x2B,TempOutH=0x2C,
"@
Set-Content "$sensorsDir\LPS25HB.cs" $f -Encoding UTF8

Write-Host "Batch 9 done (additional sensors)."

# ══════════════════════════════════════════════════════════════
# Additional popular sensors to round out the collection
# ══════════════════════════════════════════════════════════════

foreach($s in @(
    @{N="SHTC3"; Comment="Sensirion SHTC3 - humidity/temperature sensor (I2C)"},
    @{N="SCD30"; Comment="Sensirion SCD30 - CO2, humidity, temperature sensor (I2C)"}
)) {
$f = New-CmdSensor -ClassName $s.N -Comment $s.Comment `
    -Interfaces "ITemperatureSensor, IHumiditySensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal Humidity { get; set; } = 50.0m;
        public int CO2 { get; set; } = 400;
"@ `
    -WriteBody @"
            if(data.Length < 2) return;
            var cmd = (ushort)((data[0] << 8) | data[1]);
            // Generic measurement response
            var rawT = (ushort)((Temperature + 45m) * 65535m / 175m);
            var rawH = (ushort)(Humidity * 65535m / 100m);
            readBuffer = new byte[]
            {
                (byte)(rawT >> 8), (byte)(rawT & 0xFF), 0x00,
                (byte)(rawH >> 8), (byte)(rawH & 0xFF), 0x00,
            };
            readIndex = 0;
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# Additional IMU/accel sensors
foreach($s in @(
    @{N="LSM6DSOX"; Id=0x6C; Comment="ST LSM6DSOX - 6-axis IMU with MLC (I2C)"},
    @{N="BMI088_Accel"; Id=0x1E; Comment="Bosch BMI088 - accelerometer (I2C)"},
    @{N="BMA400"; Id=0x90; Comment="Bosch BMA400 - 3-axis accelerometer (I2C)"},
    @{N="LIS3MDL"; Id=0x3D; Comment="ST LIS3MDL - 3-axis magnetometer (I2C)"},
    @{N="LSM303AGR_Accelerometer"; Id=0x33; Comment="ST LSM303AGR - accelerometer (I2C)"},
    @{N="LSM303AGR_Magnetic"; Id=0x40; Comment="ST LSM303AGR - magnetometer (I2C)"},
    @{N="BMM150"; Id=0x32; Comment="Bosch BMM150 - 3-axis magnetometer (I2C)"}
)) {
$f = New-I2CSensor -ClassName $s.N -Comment $s.Comment -ChipId $s.Id -ChipIdReg 0x00 `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
        public decimal AccelerationX { get; set; }
        public decimal AccelerationY { get; set; }
        public decimal AccelerationZ { get; set; } = 1.0m;
"@ `
    -RegisterDefs @"
            Registers.ChipId.Define(this, $("0x{0:X2}" -f $s.Id));
            Registers.Status.Define(this, 0x0F);
            Registers.DataXL.Define(this).WithValueField(0,8,out axL,FieldMode.Read);
            Registers.DataXH.Define(this).WithValueField(0,8,out axH,FieldMode.Read);
            Registers.DataYL.Define(this).WithValueField(0,8,out ayL,FieldMode.Read);
            Registers.DataYH.Define(this).WithValueField(0,8,out ayH,FieldMode.Read);
            Registers.DataZL.Define(this).WithValueField(0,8,out azL,FieldMode.Read);
            Registers.DataZH.Define(this).WithValueField(0,8,out azH,FieldMode.Read);
            Registers.TempL.Define(this).WithValueField(0,8,out tL,FieldMode.Read);
            Registers.TempH.Define(this).WithValueField(0,8,out tH,FieldMode.Read);
            Registers.Ctrl1.Define(this);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var ax=(short)(AccelerationX*16384m); axL.Value=(byte)(ax&0xFF); axH.Value=(byte)(ax>>8);
            var ay=(short)(AccelerationY*16384m); ayL.Value=(byte)(ay&0xFF); ayH.Value=(byte)(ay>>8);
            var az=(short)(AccelerationZ*16384m); azL.Value=(byte)(az&0xFF); azH.Value=(byte)(az>>8);
            var rawT=(short)(Temperature*256m); tL.Value=(byte)(rawT&0xFF); tH.Value=(byte)(rawT>>8);
"@ `
    -ReadoutFields "        private IValueRegisterField axL,axH,ayL,ayH,azL,azH,tL,tH;" `
    -RegisterEnum @"
            ChipId=0x00,Status=0x1E,
            DataXL=0x28,DataXH=0x29,DataYL=0x2A,DataYH=0x2B,DataZL=0x2C,DataZH=0x2D,
            TempL=0x20,TempH=0x21,
            Ctrl1=0x10,
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# More small sensors
foreach($s in @(
    @{N="STTS22H"; Id=0xA0; Comment="ST STTS22H - digital temperature sensor (I2C)"},
    @{N="PCT2075"; Id=0x00; Comment="NXP PCT2075 - digital temperature sensor (I2C)"},
    @{N="ADS1015"; Id=0x00; Comment="TI ADS1015 - 12-bit ADC (I2C)"},
    @{N="LPS28DFW"; Id=0xB4; Comment="ST LPS28DFW - absolute pressure sensor (I2C)"},
    @{N="IIS2DLPC"; Id=0x44; Comment="ST IIS2DLPC - 3-axis accelerometer (I2C)"},
    @{N="VL53L4CD"; Id=0xEB; Comment="ST VL53L4CD - proximity/ToF sensor (I2C)"}
)) {
$f = New-I2CSensor -ClassName $s.N -Comment $s.Comment -ChipId $s.Id -ChipIdReg 0x0F `
    -Interfaces "ITemperatureSensor" `
    -Properties @"
        public decimal Temperature { get; set; } = 25.0m;
"@ `
    -RegisterDefs @"
            Registers.WhoAmI.Define(this, $("0x{0:X2}" -f $s.Id));
            Registers.Status.Define(this, 0x01);
            Registers.DataL.Define(this).WithValueField(0,8,out dataL,FieldMode.Read);
            Registers.DataH.Define(this).WithValueField(0,8,out dataH,FieldMode.Read);
            Registers.Ctrl1.Define(this);
            UpdateReadout();
"@ `
    -UpdateBody @"
            var raw = (short)(Temperature * 100m);
            dataL.Value = (byte)(raw & 0xFF);
            dataH.Value = (byte)((raw >> 8) & 0xFF);
"@ `
    -ReadoutFields "        private IValueRegisterField dataL, dataH;" `
    -RegisterEnum @"
            WhoAmI=0x0F,Status=0x27,
            DataL=0x28,DataH=0x29,
            Ctrl1=0x20,
"@
Set-Content "$sensorsDir\$($s.N).cs" $f -Encoding UTF8
}

# SPI ADC
$f = New-SPISensor -ClassName "MCP3008" `
    -Comment "Microchip MCP3008 - 10-bit 8-channel SPI ADC" `
    -Interfaces "" `
    -Properties @"
        public int Channel0 { get; set; } = 512;
        public int Channel1 { get; set; } = 256;
        public int Channel2 { get; set; }
        public int Channel3 { get; set; }
        public int Channel4 { get; set; }
        public int Channel5 { get; set; }
        public int Channel6 { get; set; }
        public int Channel7 { get; set; }
"@ `
    -TransmitBody @"
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
"@ `
    -Fields @"
        private bool startBit;
        private bool singleEnded;
        private byte channelSelect;
        private byte lastLSB;
"@
Set-Content "$sensorsDir\MCP3008.cs" $f -Encoding UTF8

# NEO_M8 - GNSS module (I2C)
$f = New-CmdSensor -ClassName "NEO_M8" `
    -Comment "u-blox NEO-M8 series - GNSS receiver (I2C)" `
    -Interfaces "" `
    -Properties @"
        public decimal Latitude { get; set; } = 48.8566m;
        public decimal Longitude { get; set; } = 2.3522m;
        public int AltitudeMm { get; set; } = 35000;
        public int NumSatellites { get; set; } = 8;
"@ `
    -WriteBody @"
            // UBX protocol: 0xB5 0x62 class id len payload checksum
            // On any write, prepare a NAV-PVT response
            var lat = (int)(Latitude * 10000000m);
            var lon = (int)(Longitude * 10000000m);
            var alt = AltitudeMm;
            readBuffer = new byte[]
            {
                0xB5, 0x62, // sync
                0x01, 0x07, // NAV-PVT
                0x5C, 0x00, // length = 92
                // iTOW (4 bytes)
                0x00, 0x00, 0x00, 0x00,
                // year, month, day, hour, min, sec
                0xE8, 0x07, 0x01, 0x01, 0x00, 0x00, 0x00,
                // valid, tAcc
                0x37, 0x00, 0x00, 0x00, 0x00,
                // fixType, flags
                0x03, 0x01, 0x0F, (byte)NumSatellites,
                // lon, lat (little-endian)
                (byte)(lon&0xFF),(byte)((lon>>8)&0xFF),(byte)((lon>>16)&0xFF),(byte)((lon>>24)&0xFF),
                (byte)(lat&0xFF),(byte)((lat>>8)&0xFF),(byte)((lat>>16)&0xFF),(byte)((lat>>24)&0xFF),
                // height, hMSL
                (byte)(alt&0xFF),(byte)((alt>>8)&0xFF),(byte)((alt>>16)&0xFF),(byte)((alt>>24)&0xFF),
                (byte)(alt&0xFF),(byte)((alt>>8)&0xFF),(byte)((alt>>16)&0xFF),(byte)((alt>>24)&0xFF),
            };
            readIndex = 0;
"@
Set-Content "$sensorsDir\NEO_M8.cs" $f -Encoding UTF8

Write-Host "Batch 10 done (remaining sensors)."

Write-Host ""
Write-Host "Total sensor files created. Listing..."
$count = (Get-ChildItem "$sensorsDir\*.cs" | Measure-Object).Count
Write-Host "Total .cs files in Sensors/: $count"
