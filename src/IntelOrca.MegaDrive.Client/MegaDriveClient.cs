#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
#pragma warning disable VSTHRD104 // Offer async methods

using System;
using System.IO;
using System.IO.Pipes;
using StreamJsonRpc;

namespace IntelOrca.MegaDrive.Client
{
    public class MegaDriveClient : IMegaDriveClient
    {
        public const string PIPE_DEFAULT_NAME = "IntelOrca.MegaDrive.Client.Pipe";

        private JsonRpc _rpc;
        private int _timeOut = 5000;

        public MegaDriveClient()
        {
            var stream = new NamedPipeClientStream(PIPE_DEFAULT_NAME);
            stream.Connect(_timeOut);
            _rpc = JsonRpc.Attach(stream);
        }

        public MegaDriveClient(Stream stream)
        {
            _rpc = JsonRpc.Attach(stream);
        }

        public void Load(string path)
        {
            _rpc.InvokeAsync(nameof(Load), path).Wait(_timeOut);
        }

        public void LoadState(string path)
        {
            _rpc.InvokeAsync(nameof(LoadState), path).Wait(_timeOut);
        }

        public void Next(int frames = 1)
        {
            _rpc.InvokeAsync(nameof(Next), frames).Wait(_timeOut);
        }

        public void ReadMemory(uint address, Span<byte> buffer)
        {
            var t = _rpc.InvokeAsync<byte[]>(nameof(ReadMemory), address, buffer.Length);
            t.Wait(_timeOut);
            t.Result.CopyTo(buffer);
        }

        public void Reset()
        {
            _rpc.InvokeAsync(nameof(Reset)).Wait(_timeOut);
        }

        public void SaveState(string path)
        {
            _rpc.InvokeAsync(nameof(SaveState), path).Wait(_timeOut);
        }

        public void SetInput(int port, uint state)
        {
            _rpc.InvokeAsync(nameof(SetInput), port, state).Wait(_timeOut);
        }

        public void WriteMemory(uint address, ReadOnlySpan<byte> buffer)
        {
            _rpc.InvokeAsync(nameof(WriteMemory), address, buffer.ToArray()).Wait(_timeOut);
        }
    }
}
