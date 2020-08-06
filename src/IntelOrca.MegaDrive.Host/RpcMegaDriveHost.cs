using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using IntelOrca.MegaDrive.Client;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using static IntelOrca.MegaDrive.Host.LibRetro;

namespace IntelOrca.MegaDrive.Host
{
    public class RpcMegaDriveHost : MegaDriveWindow, IDisposable
    {
        private NamedPipeServerStream _stream;
        private JsonRpc _rpc;
        private RpcTarget _rpcTarget;

        private readonly MegaDriveHost _host = new MegaDriveHost();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<uint> _pendingUpdates = new ConcurrentQueue<uint>();
        private ConcurrentQueue<Action> _dispatcher = new ConcurrentQueue<Action>();
        private uint _nextInput;

        private int _frameIndex;
        private int _framesToRun;
        private List<KeyValuePair<int, TaskCompletionSource<int>>> _updateTCS = new List<KeyValuePair<int, TaskCompletionSource<int>>>();

        public MegaDriveHost Host => _host;

        public RpcMegaDriveHost()
        {
            _rpcTarget = new RpcTarget(this);
            _host.Client = this;
        }

        public override void Dispose()
        {
            _host.Dispose();
            _rpc.Dispose();
            _stream.Dispose();
        }

        private void ListenUpdate()
        {
            if (_stream != null && !_stream.IsConnected)
            {
                Console.WriteLine("Client has disconnected");
                _stream.Dispose();
                _stream = null;
            }
            if (_stream == null)
            {
                _rpc?.Dispose();

                try
                {
                    Console.WriteLine("Wait for client to connect...");
                    _stream = new NamedPipeServerStream(MegaDriveClient.PIPE_DEFAULT_NAME, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    _stream.WaitForConnection();
                    Console.WriteLine("Client has connected");
                    _rpc = JsonRpc.Attach(_stream, _rpcTarget);
                    Console.WriteLine("JSON-RPC connection initiated");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Unable to listen on pipe: {ex.Message}.");
                }
            }
        }

        public void WaitForExit()
        {
            Run(new SramNone(), _host);
        }

        public void Start(string romPath)
        {
            _dispatcher.Enqueue(() =>
            {
                _frameIndex = 0;
                _framesToRun = 0;
                _host.LoadGame(romPath);
            });
        }

        public void Reset()
        {
            _dispatcher.Enqueue(() =>
            {
                _frameIndex = 0;
                _framesToRun = 0;
                _host.Reset();
            });
        }

        protected override void OnUpdate()
        {
            ListenUpdate();

            while (_dispatcher.TryDequeue(out var action))
            {
                action();
            }

            while (_pendingUpdates.TryPeek(out var input))
            {
                RefreshVideo = _pendingUpdates.Count <= 2;
                _nextInput = input;
                _host.Update();
                _pendingUpdates.TryDequeue(out _);
                _frameIndex++;
                _framesToRun--;

                foreach (var tcs in _updateTCS.ToArray())
                {
                    if (tcs.Key <= _frameIndex)
                    {
                        tcs.Value.SetResult(0);
                        _updateTCS.Remove(tcs);
                    }
                }
            }
        }

        public Task NextAsync(int count = 1, uint state = 0, int port = 0)
        {
            if (count <= 0)
            {
                return Task.CompletedTask;
            }

            var endFrame = _frameIndex + _framesToRun + count;
            Queue(count, state, port);
            var tcs = new TaskCompletionSource<int>();
            _updateTCS.Add(new KeyValuePair<int, TaskCompletionSource<int>>(endFrame, tcs));
            return tcs.Task;
        }

        public void Queue(int count = 1, uint state = 0, int port = 0)
        {
            if (count > 0)
            {
                _framesToRun += count;
                for (var i = 0; i < count; i++)
                {
                    _pendingUpdates.Enqueue(state);
                }
            }
        }

        public override bool OnInputState(uint port, uint id)
        {
            if (port == 0)
            {
                return id switch
                {
                    RETRO_DEVICE_ID_JOYPAD_UP => (_nextInput & MegaDriveClient.BUTTON_UP) != 0,
                    RETRO_DEVICE_ID_JOYPAD_DOWN => (_nextInput & MegaDriveClient.BUTTON_DOWN) != 0,
                    RETRO_DEVICE_ID_JOYPAD_LEFT => (_nextInput & MegaDriveClient.BUTTON_LEFT) != 0,
                    RETRO_DEVICE_ID_JOYPAD_RIGHT => (_nextInput & MegaDriveClient.BUTTON_RIGHT) != 0,
                    RETRO_DEVICE_ID_JOYPAD_START => (_nextInput & MegaDriveClient.BUTTON_START) != 0,
                    RETRO_DEVICE_ID_JOYPAD_Y => (_nextInput & MegaDriveClient.BUTTON_A) != 0,
                    RETRO_DEVICE_ID_JOYPAD_B => (_nextInput & MegaDriveClient.BUTTON_B) != 0,
                    RETRO_DEVICE_ID_JOYPAD_A => (_nextInput & MegaDriveClient.BUTTON_C) != 0,
                    _ => false,
                };
            }
            return false;
        }

        public class RpcTarget
        {
            private RpcMegaDriveHost _parent;

            public RpcTarget(RpcMegaDriveHost parent)
            {
                _parent = parent;
            }

            public Task LoadAsync(string path)
            {
                Console.WriteLine($"Loading {path}");
                _parent.Start(path);
                return Task.CompletedTask;
            }

            public void LoadState(string path)
            {
                throw new NotImplementedException();
            }

            public Task NextAsync(int frames = 1, uint state = 0, int port = 0)
            {
                if (frames >= 4)
                {
                    Console.WriteLine($"Next {frames}, {state}, {port}");
                }
                return _parent.NextAsync(frames, state, port);
            }

            public Task QueueAsync(int frames = 1, uint state = 0, int port = 0)
            {
                if (frames >= 4)
                {
                    Console.WriteLine($"Queue {frames}, {state}, {port}");
                }
                _parent.Queue(frames, state, port);
                return Task.CompletedTask;
            }

            public Task<byte[]> ReadMemoryAsync(uint address, int length)
            {
                var buffer = new byte[length];
                var len = _parent.Host.ReadMemory(address, buffer);
                Array.Resize(ref buffer, len);
                return Task.FromResult(buffer);
            }

            public Task ResetAsync()
            {
                _parent.Reset();
                return Task.CompletedTask;
            }

            public void SaveState(string path)
            {
                throw new NotImplementedException();
            }

            public void SetInput(int port, uint state)
            {
                _parent._nextInput = state;
            }

            public void WriteMemory(uint address, ReadOnlySpan<byte> buffer)
            {
                throw new NotImplementedException();
            }
        }
    }
}
