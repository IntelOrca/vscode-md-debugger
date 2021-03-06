﻿using System;

namespace IntelOrca.MegaDrive.Host
{
    public interface IMegaDriveClient
    {
        bool OnInputState(uint port, uint id);
        void OnVideoRefresh(Span<byte> srcPixels, int width, int height, int srcPitch);
        IntPtr OnAudioSampleBatch(IntPtr data, IntPtr frames);
    }
}
