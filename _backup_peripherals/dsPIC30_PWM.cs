//
// Copyright (c) Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// dsPIC30F Output Compare / PWM module (up to 4 channels).
// Each channel: OCxCON, OCxRS (duty secondary), OCxR (duty primary).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class dsPIC30_PWM : IWordPeripheral, IKnownSize
    {
        public dsPIC30_PWM(IMachine machine, int channels = 4)
        {
            this.channels = channels;
            occon = new ushort[channels];
            ocr   = new ushort[channels];
            ocrs  = new ushort[channels];
            IRQ = new GPIO();
            Reset();
        }

        public ushort ReadWord(long offset)
        {
            int ch = (int)(offset / ChannelStride);
            int reg = (int)(offset % ChannelStride);

            if(ch < channels)
            {
                switch(reg)
                {
                    case 0x00: return occon[ch];
                    case 0x02: return ocrs[ch];
                    case 0x04: return ocr[ch];
                }
            }
            this.Log(LogLevel.Warning, "dsPIC30_PWM: Unhandled read 0x{0:X}", offset);
            return 0;
        }

        public void WriteWord(long offset, ushort value)
        {
            int ch = (int)(offset / ChannelStride);
            int reg = (int)(offset % ChannelStride);

            if(ch < channels)
            {
                switch(reg)
                {
                    case 0x00:
                        occon[ch] = value;
                        return;
                    case 0x02:
                        ocrs[ch] = value;
                        return;
                    case 0x04:
                        ocr[ch] = value;
                        return;
                }
            }
            this.Log(LogLevel.Warning, "dsPIC30_PWM: Unhandled write 0x{0:X} = 0x{1:X}", offset, value);
        }

        public void Reset()
        {
            for(int i = 0; i < channels; i++)
            {
                occon[i] = 0;
                ocr[i] = 0;
                ocrs[i] = 0;
            }
        }

        public long Size => ChannelStride * channels;
        public GPIO IRQ { get; }

        private const int ChannelStride = 0x06; // 3 x 16-bit regs per channel
        private readonly int channels;
        private readonly ushort[] occon;
        private readonly ushort[] ocr;
        private readonly ushort[] ocrs;
    }
}
