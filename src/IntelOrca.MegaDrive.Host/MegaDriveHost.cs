using System;
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
        private ImmutableArray<uint> _debuggerBreakpoints;
        private uint? _stepOverBreakpoint;
        private uint? _stepOutTargetSP;
        private int _breakIn;
        private M68K _m68k;
        private volatile bool _debuggerRunning = true;

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
                        var addr = s.Substring(1, s.Length - 2);
                        if (addr.StartsWith("0x"))
                        {
                            addr = addr.Substring(2);
                            var result = ReadMemory(Convert.ToUInt32(addr, 16));
                            if (result.HasValue)
                            {
                                return result.ToString();
                            }
                        }
                        else
                        {
                            var result = ReadMemory(Convert.ToUInt32(addr));
                            if (result.HasValue)
                            {
                                return result.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
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

        private int? ReadMemory(uint address)
        {
            if ((address & 0xFFFF0000) != 0)
            {
                address &= 0xFFFF;
                var mem = retro_get_memory_data(RETRO_MEMORY_SYSTEM_RAM);
                if (mem != IntPtr.Zero)
                {
                    return Marshal.ReadInt16(mem, (int)address);
                }
            }
            else
            {
                var mem = retro_get_memory_data(256);
                if (mem != IntPtr.Zero)
                {
                    return Marshal.ReadInt16(mem, (int)address);
                }
            }
            return null;
        }

        private void OnDebugM68k(IntPtr pM68K)
        {
            _m68k = new M68K(pM68K);
            _debuggerPC = _m68k.PC;
            if (_debuggerPC == _stepOverBreakpoint)
            {
                _stepOverBreakpoint = null;
                Pause("step");
            }
            else if (!_debuggerBreakpoints.IsDefaultOrEmpty && _debuggerBreakpoints.Contains(_debuggerPC))
            {
                Pause("breakpoint");
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

            // example breakpoint: 0xF070
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
}
