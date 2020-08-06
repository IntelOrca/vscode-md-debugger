using System;
using System.Threading.Tasks;

namespace IntelOrca.MegaDrive.Client
{
    public interface IMegaDriveClient : IDisposable
    {
        Task LoadAsync(string path);
        Task ResetAsync();
        Task NextAsync(int frames = 1, uint state = 0, int port = 0);
        Task QueueAsync(int frames = 1, uint state = 0, int port = 0);
        Task<byte[]> ReadMemoryAsync(uint address, int length);
        Task WriteMemoryAsync(uint address, byte[] buffer);
        Task LoadStateAsync(string path);
        Task SaveStateAsync(string path);
    }
}
