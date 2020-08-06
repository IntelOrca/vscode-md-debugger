using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using static IntelOrca.MegaDrive.Host.LibRetro;

namespace IntelOrca.MegaDrive.Host
{
    public class MegaDriveHost : IDisposable, IMegaDriveDebugController
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DebugM68Kt(IntPtr m68k);

        private readonly IntPtr _strptrEmpty;
        private readonly IntPtr _strptrSystem;
        private readonly List<Delegate> _savedDelegates = new List<Delegate>();
        private bool _disposed;
        private retro_system_av_info _avInfo;

        public IMegaDriveClient Client { get; set; }
        public double FPS => _avInfo.timing.fps;
        public double SampleRate => _avInfo.timing.sample_rate;

        /// <summary>
        /// Stores a delegate so that it does not get garbage collected until the instance is disposed.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="d"></param>
        /// <returns></returns>
        private T SaveDelegate<T>(T d) where T : Delegate
        {
            _savedDelegates.Add(d);
            return d;
        }

        public MegaDriveHost()
        {
            _strptrEmpty = Marshal.StringToHGlobalAnsi("");
            _strptrSystem = Marshal.StringToHGlobalAnsi("mega drive / genesis");

            retro_get_system_info(out var info);
            retro_set_environment(SaveDelegate<retro_environment_t>(OnEnvironment));
            retro_set_input_poll(SaveDelegate<retro_input_poll_t>(OnInputPoll));
            retro_set_input_state(SaveDelegate<retro_input_state_t>(OnInputState));
            retro_set_video_refresh(SaveDelegate<retro_video_refresh_t>(OnVideoRefresh));
            retro_set_audio_sample_batch(SaveDelegate<retro_audio_sample_batch_t>(OnAudioSampleBatch));
            retro_init();
        }

        ~MegaDriveHost() => Dispose();

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                retro_deinit();
                Marshal.FreeHGlobal(_strptrEmpty);
                Marshal.FreeHGlobal(_strptrSystem);
            }
        }

        public void LoadGame(string path)
        {
            var game = new retro_game_info
            {
                path = Marshal.StringToHGlobalAnsi(path)
            };
            try
            {
                if (!retro_load_game(ref game))
                {
                    throw new Exception($"Unable to load game: {path}");
                }
                retro_get_system_av_info(out _avInfo);
            }
            finally
            {
                Marshal.FreeHGlobal(game.path);
            }
        }

        public void Update()
        {
            retro_run();
        }

        private bool OnEnvironment(uint cmd, IntPtr data)
        {
            switch (cmd)
            {
                case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
                    var format = Marshal.PtrToStructure<int>(data);
                    return (format == RETRO_PIXEL_FORMAT_RGB565);
                case RETRO_ENVIRONMENT_GET_VARIABLE:
                    var var = Marshal.PtrToStructure<retro_variable>(data);
                    var key = Marshal.PtrToStringAnsi(var.key);
                    var value = Marshal.PtrToStringAnsi(var.value);
                    switch (key)
                    {
                        case "genesis_plus_gx_system_hw":
                            var.value = _strptrSystem;
                            break;
                        case "ted_debug_m68k":
                            var.value = Marshal.GetFunctionPointerForDelegate(SaveDelegate<DebugM68Kt>(OnDebugM68k));
                            break;
                        default:
                            var.value = _strptrEmpty;
                            break;
                    }
                    Marshal.StructureToPtr(var, data, fDeleteOld: false);
                    return true;
                default:
                    return false;
            }
        }

        private void OnInputPoll()
        {
        }

        private short OnInputState(uint port, uint device, uint index, uint id)
        {
            if (device == RETRO_DEVICE_JOYPAD)
            {
                return (short)(Client.OnInputState(port, id) ? 1 : 0);
            }
            return 0;
        }

        private unsafe void OnVideoRefresh(IntPtr srcPixels, uint width, uint height, IntPtr srcPitch)
        {
            var src = new Span<byte>((byte*)srcPixels, (int)srcPitch * (int)height);
            Client.OnVideoRefresh(src, (int)width, (int)height, (int)srcPitch);
        }

        private IntPtr OnAudioSampleBatch(IntPtr data, IntPtr frames)
        {
            return Client.OnAudioSampleBatch(data, frames);
        }

        internal unsafe Span<byte> GetSaveRAM()
        {
            var sramSize = (int)retro_get_memory_size(RETRO_MEMORY_SAVE_RAM);
            var sramData = retro_get_memory_data(RETRO_MEMORY_SAVE_RAM);
            if (sramData != IntPtr.Zero)
            {
                var s = (byte*)sramData;
                return new Span<byte>(s, sramSize);
            }
            return null;
        }

        internal void Reset()
        {
            throw new NotImplementedException();
        }

        #region Debugger

        private event EventHandler<string> _onStateChanged;
        private uint _debuggerPC;
        private uint _debuggerSP;
        private ImmutableArray<uint> _debuggerBreakpoints;
        private uint? _stepOverBreakpoint;
        private uint? _stepOutTargetSP;
        private int _breakIn;
        private M68K _m68k;
        private Stack<MegaDriveStackFrame> _callStack = new Stack<MegaDriveStackFrame>();
        private bool _nextInstructionIsBranch;
        private bool _nextInstructionIsReturn;
        private volatile bool _debuggerRunning = true;

        public Stack<MegaDriveStackFrame> CallStack => _callStack;

        private void Resume()
        {
            if (!_debuggerRunning)
            {
                _debuggerRunning = true;
                _onStateChanged?.Invoke(this, null);
            }
        }

        private void Pause(string reason)
        {
            if (_debuggerRunning)
            {
                _debuggerRunning = false;
                _onStateChanged?.Invoke(this, reason);
            }
        }

        event EventHandler<string> IMegaDriveDebugController.OnStateChanged
        {
            add { _onStateChanged += value; }
            remove { _onStateChanged -= value; }
        }

        ImmutableArray<uint> IMegaDriveDebugController.Breakpoints
        {
            get => _debuggerBreakpoints;
            set => _debuggerBreakpoints = value;
        }

        uint IMegaDriveDebugController.SP => _m68k.GetRegister(M68K_REG.A7);
        uint IMegaDriveDebugController.PC => _debuggerPC;

        bool IMegaDriveDebugController.Running => _debuggerRunning;

        void IMegaDriveDebugController.Break()
        {
            Pause("pause");
            _stepOutTargetSP = null;
            _stepOverBreakpoint = null;
        }

        void IMegaDriveDebugController.Resume()
        {
            Resume();
        }

        void IMegaDriveDebugController.Step(int n)
        {
            _breakIn = n;
            Resume();
        }

        void IMegaDriveDebugController.StepOver()
        {
            var len = GetInstructionLength(_debuggerPC);
            if (len == 0)
            {
                _breakIn = 1;
            }
            else
            {
                _stepOverBreakpoint = _debuggerPC + (uint)len;
            }
            Resume();
        }

        void IMegaDriveDebugController.StepOut()
        {
            var sp = _m68k.GetRegister(M68K_REG.A7);
            _stepOutTargetSP = sp + 4;
            Resume();
        }

        string IMegaDriveDebugController.Evaluate(string s)
        {
            try
            {
                s = s.Trim();

                var display = "w";
                var lastCommaIndex = s.LastIndexOf(',');
                if (lastCommaIndex != -1)
                {
                    display = s.Substring(lastCommaIndex + 1).Trim();
                    s = s.Substring(0, lastCommaIndex).Trim();
                }

                if (s.Equals("SP", StringComparison.OrdinalIgnoreCase))
                {
                    s = "A7";
                }
                if (s.Equals("PC", StringComparison.OrdinalIgnoreCase))
                {
                    return $"0x{_debuggerPC:X8}";
                }
                else
                {
                    var registerNames = Enum.GetNames(typeof(M68K_REG));
                    var registerIndex = Array.IndexOf(registerNames, s.ToString());
                    if (registerIndex != -1)
                    {
                        var reg = (M68K_REG)registerIndex;
                        var value = _m68k.GetRegister(reg);
                        return $"0x{value:X8}";
                    }
                    else if (s.StartsWith("[") && s.EndsWith("]"))
                    {
                        var addr = ParseAddress(s.Substring(1, s.Length - 2));
                        long? memResult = null;
                        bool unsigned = display.Contains("h");
                        if (display.Contains("l"))
                        {
                            if (ReadMemory32(addr) is int r)
                                memResult = unsigned ? (uint)r : (long)r;
                        }
                        else if (display.Contains("w"))
                        {
                            if (ReadMemory16(addr) is short r)
                                memResult = unsigned ? (ushort)r : (long)r;
                        }
                        else if (display.Contains("b"))
                        {
                            memResult = ReadMemory8(addr);
                        }
                        if (memResult != null)
                        {
                            return display.Contains("h")
                                ? "0x" + memResult.Value.ToString("X2")
                                : memResult.Value.ToString();
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private uint ParseAddress(string szAddress)
        {
            return szAddress.StartsWith("0x")
                ? Convert.ToUInt32(szAddress.Substring(2), 16)
                : Convert.ToUInt32(szAddress);
        }

        string IMegaDriveDebugController.SetExpression(string s, string valueS)
        {
            try
            {
                uint value = ParseNumber(valueS);
                if (s.Equals("SP", StringComparison.OrdinalIgnoreCase))
                {
                    s = "A7";
                }
                if (s.Equals("PC", StringComparison.OrdinalIgnoreCase))
                {
                    _m68k.PC = value;
                }
                else
                {
                    var registerNames = Enum.GetNames(typeof(M68K_REG));
                    var registerIndex = Array.IndexOf(registerNames, s.ToString());
                    if (registerIndex != -1)
                    {
                        var reg = (M68K_REG)registerIndex;
                        _m68k.SetRegister(reg, value);
                    }
                }
                return $"0x{value:X8}";
            }
            catch
            {
            }
            return null;
        }

        private uint ParseNumber(string s)
        {
            if (s.StartsWith("0x"))
            {
                s = s.Substring(2);
                return Convert.ToUInt32(s, 16);
            }
            else
            {
                return Convert.ToUInt32(s);
            }
        }

        private IntPtr? GetMemoryPointer(uint address)
        {
            var result = IntPtr.Zero;
            if ((address & 0xFF000000) != 0)
            {
                address &= 0xFFFF;
                if (address < (uint)retro_get_memory_size(RETRO_MEMORY_SYSTEM_RAM))
                {
                    result = retro_get_memory_data(RETRO_MEMORY_SYSTEM_RAM);
                    if (result != IntPtr.Zero)
                    {
                        result += (int)address;
                    }
                }
            }
            else
            {
                if (address < (uint)retro_get_memory_size(256))
                {
                    result = retro_get_memory_data(256);
                    if (result != IntPtr.Zero)
                    {
                        result += (int)address;
                    }
                }
            }
            return result == IntPtr.Zero ? (IntPtr?)null : result;
        }

        public int? ReadMemory32(uint address)
        {
            return GetMemoryPointer(address) is IntPtr p
                ? WordSwap(Marshal.ReadInt32(p, 0))
                : (int?)null;
        }

        private static int WordSwap(int value)
        {
            return (int)(((uint)value << 16) | ((uint)value >> 16));
        }

        public short? ReadMemory16(uint address)
        {
            return GetMemoryPointer(address) is IntPtr p
                ? Marshal.ReadInt16(p, 0)
                : (short?)null;
        }

        public byte? ReadMemory8(uint address)
        {
            return GetMemoryPointer(address) is IntPtr p
                ? Marshal.ReadByte(p, 0)
                : (byte?)null;
        }

        private void OnDebugM68k(IntPtr pM68K)
        {
            _m68k = new M68K(pM68K);
            _debuggerPC = _m68k.PC;
            _debuggerSP = _m68k.GetRegister(M68K_REG.A7);

            if (_nextInstructionIsBranch)
            {
                _nextInstructionIsBranch = false;
                _callStack.Push(new MegaDriveStackFrame()
                {
                    SP = _debuggerSP,
                    SubroutineAddress = _debuggerPC,
                    ReturnAddress = (uint)(ReadMemory32(_debuggerSP) ?? 0)
                });
            }
            else if (_nextInstructionIsReturn)
            {
                _nextInstructionIsReturn = false;
                if (_callStack.Count > 0)
                {
                    _callStack.Pop();
                }
            }

            // If SP is manually edited, correct the call stack
            while (_callStack.Count > 0 && _debuggerSP > _callStack.Peek().SP)
            {
                if (_callStack.Count > 0)
                {
                    _callStack.Pop();
                }
            }

            if (_debuggerPC == _stepOverBreakpoint)
            {
                _stepOverBreakpoint = null;
                Pause("step");
            }
            else if (!_debuggerBreakpoints.IsDefaultOrEmpty && _debuggerBreakpoints.Contains(_debuggerPC))
            {
                Pause("breakpoint");
            }

            if (ReadMemory16(_debuggerPC) is short instr)
            {
                if ((instr & 0b11111111_00000000) == 0b01100001_00000000 || // BSR
                    (instr & 0b1111111111_000000) == 0b0100111010_000000)   // JSR
                {
                    _nextInstructionIsBranch = true;
                }
                else if (instr == 0b0100111001110101)
                {
                    _nextInstructionIsReturn = true;
                }
            }

            if (_stepOutTargetSP.HasValue)
            {
                var sp = _m68k.GetRegister(M68K_REG.A7);
                if (sp >= _stepOutTargetSP)
                {
                    _stepOutTargetSP = null;
                    Pause("step");
                }
            }

            while (!_debuggerRunning)
            {
                // Halt thread until told to run again
                Thread.Sleep(100);
            }

            if (_breakIn > 0)
            {
                _breakIn--;
                if (_breakIn == 0)
                {
                    // Break on next instruction
                    Pause("step");
                }
            }
        }

        private static System.Text.StringBuilder sb = new System.Text.StringBuilder();
        private void RecordSonicInfo()
        {
            var time = (ushort)ReadMemory16(0xFFFFFE04);
            if (time >= 1 && time <= 1630)
            {
                var x = (short)ReadMemory16(0xFFFFB008);
                var xx = (ushort)ReadMemory16(0xFFFFB00A);
                var y = (short)ReadMemory16(0xFFFFB00C);
                var yy = (ushort)ReadMemory16(0xFFFFB00E);
                var vx = (short)ReadMemory16(0xFFFFB010);
                var vy = (short)ReadMemory16(0xFFFFB012);
                var gv = (short)ReadMemory16(0xFFFFB014);
                var ang = (byte)ReadMemory8(0xFFFFB027);
                sb.Append($"{time},{x},0x{xx:X4},{y},0x{yy:X4},{vx},{vy},{gv},0x{ang:X2}\n");
            }
            else if (time == 1631)
            {
                System.IO.File.WriteAllText(@"C:\Users\Ted\Desktop\data.txt", sb.ToString());
            }
        }

        private int GetInstructionLength(uint address)
        {
            const int JSR_MASK = 0b1111111111_000_000;
            const int JSR = 0b0100111010_000_000;
            const int BSR_MASK = 0xFF00;
            const int BSR = 0b01100001_00000000;

            var romAddr = retro_get_memory_data(256);
            if (romAddr != IntPtr.Zero)
            {
                var instr0 = (ushort)Marshal.ReadInt16(romAddr, (int)(address + 0));
                var instr1 = (ushort)Marshal.ReadInt16(romAddr, (int)(address + 2));
                if ((instr0 & JSR_MASK) == JSR)
                {
                    int size = 2;
                    if ((instr0 & 0b111_000) == 0b111_000)
                    {
                        size += 2;
                        if ((instr0 & 0b111) == 1)
                        {
                            size += 2;
                        }
                    }
                    return size;
                }
                else if ((instr0 & BSR_MASK) == BSR)
                {
                    int size = 2;
                    if ((instr0 & 0x00FF) == 0)
                    {
                        size += 2;
                    }
                    else if ((instr0 & 0x00FF) == 0xFF)
                    {
                        size += 4;
                    }
                    return size;
                }
            }
            return 0;
        }

        #endregion
    }

    public struct MegaDriveStackFrame
    {
        public uint SP { get; set; }
        public uint SubroutineAddress { get; set; }
        public uint ReturnAddress { get; set; }
    }
}
