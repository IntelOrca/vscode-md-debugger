using System;
using System.Runtime.InteropServices;
using System.Threading;
using static IntelOrca.MegaDrive.Host.LibRetro;
using static SDL2.SDL;

namespace IntelOrca.MegaDrive.Host
{
    public class MegaDriveWindow : IDisposable, IMegaDriveClient
    {
        private const int MD_WIDTH = 320;
        private const int MD_HEIGHT = 224;
        private const int CURSOR_HIDE_DELAY = 2000;
        private const int CONTROLLER_CHECK_INTERVAL = 5000;
        private const int SRAM_SAVE_INTERVAL = 5000;

        private bool _disposed;
        private readonly IntPtr _window;
        private readonly IntPtr _renderer;
        private readonly IntPtr _emulationTexture;

        private byte[] _keyboardState;
        private int _keyboardStateCount;
        private readonly IntPtr[] _controllers = new IntPtr[4];
        private int _lastControllerCount;
        private uint _lastControllerCheck;

        private uint _audioDevice;

        private bool _refreshVideo;
        private uint _lastCursorMoveTick;
        private uint _lastSramSaveTick;

        public MegaDriveWindow()
        {
            SDL_SetHintWithPriority(SDL_HINT_RENDER_SCALE_QUALITY, "0", SDL_HintPriority.SDL_HINT_OVERRIDE);

            _window = InitialiseWindow();
            _renderer = SDL_CreateRenderer(_window, 0, SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);
            _emulationTexture = SDL_CreateTexture(_renderer, SDL_PIXELFORMAT_RGB565, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 320, 224);

            // Hide the cursor
            SDL_ShowCursor(0);

            InitialiseControllers();
        }

        ~MegaDriveWindow()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                DisposeAudio();
                SDL_DestroyTexture(_emulationTexture);
                SDL_DestroyRenderer(_renderer);
                SDL_DestroyWindow(_window);
                SDL_Quit();
            }
        }

        private IntPtr InitialiseWindow()
        {
            SDL_Init(SDL_INIT_EVERYTHING);
            SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "2");

            // Find the best window size for the screen
            var (w, h) = GetBestSize();
            var x = SDL_WINDOWPOS_CENTERED_DISPLAY(1);
            var y = SDL_WINDOWPOS_CENTERED_DISPLAY(1);

            // Create the window, renderer etc.
            var windowFlags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI;
            var window = SDL_CreateWindow("MEGADRIVE EMULATOR", x, y, w, h, windowFlags);
            SetIcon(window);
            return window;
        }

        private static void SetIcon(IntPtr window)
        {
        }

        private static (int, int) GetBestSize()
        {
            int w = MD_WIDTH;
            int h = MD_HEIGHT;
            if (SDL_GetDesktopDisplayMode(0, out var displayMode) == 0)
            {
                while (true)
                {
                    int nw = w * 2;
                    int nh = h * 2;
                    if (nw > displayMode.w || nh > displayMode.h)
                    {
                        break;
                    }
                    else
                    {
                        w = nw;
                        h = nh;
                    }
                }
            }
            return (w, h);
        }

        private void ToggleFullscreen()
        {
            var flags = (SDL_WindowFlags)SDL_GetWindowFlags(_window);
            if ((flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0)
            {
                SDL_SetWindowFullscreen(_window, 0);
            }
            else
            {
                SDL_SetWindowFullscreen(_window, (uint)SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);
            }
        }

        private void InitialiseControllers()
        {
            int numJoysticks = SDL_NumJoysticks();
            int cindex = 0;
            for (int i = 0; i < numJoysticks; i++)
            {
                if (SDL_IsGameController(i) != SDL_bool.SDL_FALSE)
                {
                    var controller = SDL_GameControllerOpen(i);
                    if (controller != IntPtr.Zero)
                    {
                        _controllers[cindex++] = controller;
                        if (cindex == _controllers.Length)
                        {
                            break;
                        }
                    }
                }
            }
            _lastControllerCount = numJoysticks;
        }

        private void CloseControllers()
        {
            for (int i = 0; i < _controllers.Length; i++)
            {
                var controller = _controllers[i];
                if (controller != IntPtr.Zero)
                {
                    SDL_GameControllerClose(controller);
                    _controllers[i] = IntPtr.Zero;
                }
            }
        }

        private void RecheckControllers()
        {
            bool reinitRequired = false;

            // Check if the number of connected gamepads has changed
            int numJoysticks = SDL_NumJoysticks();
            if (numJoysticks != _lastControllerCount)
            {
                reinitRequired = true;
            }
            else
            {
                // Check if any of the controllers have been detached
                for (int i = 0; i < _controllers.Length; i++)
                {
                    var controller = _controllers[i];
                    if (controller != IntPtr.Zero)
                    {
                        if (SDL_GameControllerGetAttached(controller) == SDL_bool.SDL_FALSE)
                        {
                            reinitRequired = true;
                            break;
                        }
                    }
                }
            }

            if (reinitRequired)
            {
                CloseControllers();
                InitialiseControllers();
            }
        }

        private void InitialiseAudio(int sampleRate)
        {
            DisposeAudio();
            var want = new SDL_AudioSpec
            {
                freq = sampleRate,
                format = AUDIO_S16,
                channels = 2
            };
            _audioDevice = SDL_OpenAudioDevice(null, 0, ref want, out var have, 0);
            SDL_PauseAudioDevice(_audioDevice, 0);
        }

        private void DisposeAudio()
        {
            SDL_CloseAudioDevice(_audioDevice);
        }

        public void Run(Sram sram, MegaDriveHost host, CancellationToken ct = default)
        {
            InitialiseAudio((int)host.SampleRate);

            host.Client = this;
            var tickWait = (uint)Math.Floor(1000.0 / host.FPS);
            var lastTicks = 0U;
            var lastRefreshVideo = 0U;
            var quit = false;
            var ff = false;
            while (!quit && !ct.IsCancellationRequested)
            {
                var ticks = SDL_GetTicks();
                var diff = ticks - lastTicks;
                if (diff >= tickWait || ff)
                {
                    lastTicks = ticks;
                    if (!ff || ticks > lastRefreshVideo + 30)
                    {
                        _refreshVideo = true;
                        lastRefreshVideo = ticks;
                    }
                    else
                    {
                        _refreshVideo = false;
                    }

                    if (_lastCursorMoveTick + CURSOR_HIDE_DELAY < ticks)
                    {
                        SDL_ShowCursor(0);
                    }
                    else
                    {
                        SDL_ShowCursor(1);
                    }

                    if (_lastControllerCheck + CONTROLLER_CHECK_INTERVAL < ticks)
                    {
                        _lastControllerCheck = ticks;
                        RecheckControllers();
                    }

                    if (_lastSramSaveTick + SRAM_SAVE_INTERVAL < ticks)
                    {
                        _lastSramSaveTick = ticks;
                        if (sram.Update())
                        {
                            sram.Save();
                        }
                    }

                    SDL_PumpEvents();
                    UpdateKeyboardState();
                    host.Update();

                    ff = GetKeyState(SDL_Scancode.SDL_SCANCODE_BACKSPACE);
                    if (ff)
                    {
                        SDL_ClearQueuedAudio(_audioDevice);
                    }

                    quit = ProcessEvents(host, ticks);
                }
                else
                {
                    SDL_Delay(tickWait - diff);
                }
            }
        }

        private bool ProcessEvents(MegaDriveHost host, uint ticks)
        {
            var quit = false;
            while (SDL_PollEvent(out var e) != 0)
            {
                switch (e.type)
                {
                    case SDL_EventType.SDL_QUIT:
                        quit = true;
                        break;
                    case SDL_EventType.SDL_MOUSEMOTION:
                        _lastCursorMoveTick = ticks;
                        break;
                    case SDL_EventType.SDL_KEYDOWN:
                        var keysym = e.key.keysym;
                        switch (keysym.scancode)
                        {
                            case SDL_Scancode.SDL_SCANCODE_R:
                                if ((keysym.mod & SDL_Keymod.KMOD_LCTRL) != 0 ||
                                    (keysym.mod & SDL_Keymod.KMOD_RCTRL) != 0)
                                {
                                    host.Reset();
                                }
                                break;
                            case SDL_Scancode.SDL_SCANCODE_RETURN:
                                if (GetKeyState(SDL_Scancode.SDL_SCANCODE_LALT) || GetKeyState(SDL_Scancode.SDL_SCANCODE_RALT))
                                {
                                    ToggleFullscreen();
                                }
                                break;
                        }
                        break;
                }
            }
            return quit;
        }

        private void UpdateKeyboardState()
        {
            var keys = SDL_GetKeyboardState(out _keyboardStateCount);
            if (keys != IntPtr.Zero)
            {
                Array.Resize(ref _keyboardState, _keyboardStateCount);
                Marshal.Copy(keys, _keyboardState, 0, _keyboardStateCount);
            }
        }

        private bool GetKeyState(SDL_Scancode key)
        {
            var index = (int)key;
            if (index >= _keyboardStateCount || index >= _keyboardState.Length)
            {
                return false;
            }
            return _keyboardState[index] != 0;
        }

        public bool OnInputState(uint port, uint id)
        {
            return
                OnInputStateController(port, id) ||
                OnInputStateKeyboard(port, id);
        }

        public bool OnInputStateKeyboard(uint port, uint id)
        {
            if (port == 0)
            {
                switch (id)
                {
                    case RETRO_DEVICE_ID_JOYPAD_UP: return GetKeyState(SDL_Scancode.SDL_SCANCODE_UP);
                    case RETRO_DEVICE_ID_JOYPAD_DOWN: return GetKeyState(SDL_Scancode.SDL_SCANCODE_DOWN);
                    case RETRO_DEVICE_ID_JOYPAD_LEFT: return GetKeyState(SDL_Scancode.SDL_SCANCODE_LEFT);
                    case RETRO_DEVICE_ID_JOYPAD_RIGHT: return GetKeyState(SDL_Scancode.SDL_SCANCODE_RIGHT);
                    case RETRO_DEVICE_ID_JOYPAD_START: return GetKeyState(SDL_Scancode.SDL_SCANCODE_RETURN);
                    case RETRO_DEVICE_ID_JOYPAD_Y: return GetKeyState(SDL_Scancode.SDL_SCANCODE_A);
                    case RETRO_DEVICE_ID_JOYPAD_B: return GetKeyState(SDL_Scancode.SDL_SCANCODE_S);
                    case RETRO_DEVICE_ID_JOYPAD_A: return GetKeyState(SDL_Scancode.SDL_SCANCODE_D);
                }
            }
            return false;
        }

        public bool OnInputStateController(uint port, uint id)
        {
            const int AXIS_TOLERANCE = (0x7FFF * 1) / 4;
            const int TRIGGER_TOLERANCE = (0x7FFF * 1) / 4;

            if (port < 4)
            {
                var controller = _controllers[port];
                if (controller != IntPtr.Zero)
                {
                    switch (id)
                    {
                        case RETRO_DEVICE_ID_JOYPAD_UP:
                            return
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP) != 0 ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY) < -AXIS_TOLERANCE ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY) < -AXIS_TOLERANCE;
                        case RETRO_DEVICE_ID_JOYPAD_DOWN:
                            return
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN) != 0 ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY) > AXIS_TOLERANCE ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY) > AXIS_TOLERANCE;
                        case RETRO_DEVICE_ID_JOYPAD_LEFT:
                            return
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT) != 0 ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX) < -AXIS_TOLERANCE ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX) < -AXIS_TOLERANCE;
                        case RETRO_DEVICE_ID_JOYPAD_RIGHT:
                            return
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT) != 0 ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX) > AXIS_TOLERANCE ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX) > AXIS_TOLERANCE;

                        case RETRO_DEVICE_ID_JOYPAD_START:
                            return SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START) != 0;

                        case RETRO_DEVICE_ID_JOYPAD_Y:
                            return
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X) != 0 ||
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER) != 0 ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT) > TRIGGER_TOLERANCE;

                        case RETRO_DEVICE_ID_JOYPAD_B:
                            return
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) != 0 ||
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y) != 0 ||
                                SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER) != 0 ||
                                SDL_GameControllerGetAxis(controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT) > TRIGGER_TOLERANCE;

                        case RETRO_DEVICE_ID_JOYPAD_A:
                            return SDL_GameControllerGetButton(controller, SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) != 0;
                    }
                }
            }
            return false;
        }

        public unsafe void OnVideoRefresh(Span<byte> srcPixels, int width, int height, int srcPitch)
        {
            if (!_refreshVideo)
            {
                return;
            }

            if (SDL_LockTexture(_emulationTexture, IntPtr.Zero, out var dstPixelsP, out var dstPitch) == 0)
            {
                var dstPixels = new Span<byte>((byte*)dstPixelsP, dstPitch * height);
                if (dstPitch == srcPitch)
                {
                    srcPixels
                        .Slice(0, height * srcPitch)
                        .CopyTo(dstPixels);
                }
                else
                {
                    var src = srcPixels;
                    var dst = dstPixels;
                    for (int y = 0; y < height; y++)
                    {
                        src.Slice(0, width * 2).CopyTo(dst);
                        src = src.Slice(srcPitch);
                        dst = dst.Slice(dstPitch);
                    }
                }
                SDL_UnlockTexture(_emulationTexture);


                var srcRect = new SDL_Rect
                {
                    w = width,
                    h = height
                };
                SDL_RenderGetViewport(_renderer, out var maxRect);

                var dstRect = maxRect;
                var scaleX = (float)maxRect.w / width;
                var scaleY = (float)maxRect.h / height;
                if (scaleX == scaleY)
                {
                }
                else if (scaleX > scaleY)
                {
                    dstRect.w = (int)(width * scaleY);
                    dstRect.x = (maxRect.w - dstRect.w) / 2;
                }
                else
                {
                    dstRect.h = (int)(height * scaleX);
                    dstRect.y = (maxRect.h - dstRect.h) / 2;
                }

                SDL_RenderCopy(_renderer, _emulationTexture, ref srcRect, ref dstRect);
                SDL_RenderPresent(_renderer);
            }
        }

        public IntPtr OnAudioSampleBatch(IntPtr data, IntPtr frames)
        {
            if (data != IntPtr.Zero && frames != IntPtr.Zero)
            {
                SDL_QueueAudio(_audioDevice, data, (uint)frames * 4);
            }
            return frames;
        }
    }
}
