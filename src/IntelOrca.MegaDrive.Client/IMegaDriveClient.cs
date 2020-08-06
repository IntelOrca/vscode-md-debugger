using System;

namespace IntelOrca.MegaDrive.Client
{
    public interface IMegaDriveClient
    {
        void Load(string path);
        void Reset();
        void Next(int frames = 1);
        void SetInput(int port, uint state);
        void ReadMemory(uint address, Span<byte> buffer);
        void WriteMemory(uint address, ReadOnlySpan<byte> buffer);
        void LoadState(string path);
        void SaveState(string path);
    }
}
