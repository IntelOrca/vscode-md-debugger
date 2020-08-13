#pragma warning disable IDE1006 // Naming Styles

using System;
using System.Runtime.InteropServices;

namespace IntelOrca.MegaDrive.Host
{
    internal static class LibRetro
    {
        private const string LibraryFileName = "genesis_plus_gx_libretro.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct retro_system_info
        {
            public IntPtr library_name;
            public IntPtr library_version;
            public IntPtr valid_extensions;
            public bool need_fullpath;
            public bool block_extract;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct retro_variable
        {
            public IntPtr key;
            public IntPtr value;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct retro_game_info
        {
            public IntPtr path;
            public IntPtr data;
            public IntPtr size;
            public IntPtr meta;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct retro_game_geometry
        {
            public uint base_width;
            public uint base_height;
            public uint max_width;
            public uint max_height;
            public float aspect_ratio;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct retro_system_timing
        {
            public double fps;
            public double sample_rate;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct retro_system_av_info
        {
            public retro_game_geometry geometry;
            public retro_system_timing timing;
        };

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate bool retro_environment_t(uint cmd, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void retro_video_refresh_t(IntPtr data, uint width, uint height, IntPtr pitch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void retro_audio_sample_t(short left, short right);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr retro_audio_sample_batch_t(IntPtr data, IntPtr frames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void retro_input_poll_t();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate short retro_input_state_t(uint port, uint device, uint index, uint id);

        public const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10;
        public const uint RETRO_ENVIRONMENT_GET_VARIABLE = 15;
        public const uint RETRO_MEMORY_SAVE_RAM = 0;
        public const uint RETRO_MEMORY_RTC = 1;
        public const uint RETRO_MEMORY_SYSTEM_RAM = 2;
        public const uint RETRO_MEMORY_VIDEO_RAM = 3;

        public const int RETRO_PIXEL_FORMAT_0RGB1555 = 0;
        public const int RETRO_PIXEL_FORMAT_XRGB8888 = 1;
        public const int RETRO_PIXEL_FORMAT_RGB565 = 2;
        public const int RETRO_PIXEL_FORMAT_UNKNOWN = int.MaxValue;

        public const int RETRO_DEVICE_NONE = 0;
        public const int RETRO_DEVICE_JOYPAD = 1;

        public const int RETRO_DEVICE_ID_JOYPAD_B = 0;
        public const int RETRO_DEVICE_ID_JOYPAD_Y = 1;
        public const int RETRO_DEVICE_ID_JOYPAD_SELECT = 2;
        public const int RETRO_DEVICE_ID_JOYPAD_START = 3;
        public const int RETRO_DEVICE_ID_JOYPAD_UP = 4;
        public const int RETRO_DEVICE_ID_JOYPAD_DOWN = 5;
        public const int RETRO_DEVICE_ID_JOYPAD_LEFT = 6;
        public const int RETRO_DEVICE_ID_JOYPAD_RIGHT = 7;
        public const int RETRO_DEVICE_ID_JOYPAD_A = 8;
        public const int RETRO_DEVICE_ID_JOYPAD_X = 9;
        public const int RETRO_DEVICE_ID_JOYPAD_L = 10;
        public const int RETRO_DEVICE_ID_JOYPAD_R = 11;
        public const int RETRO_DEVICE_ID_JOYPAD_L2 = 12;
        public const int RETRO_DEVICE_ID_JOYPAD_R2 = 13;
        public const int RETRO_DEVICE_ID_JOYPAD_L3 = 14;
        public const int RETRO_DEVICE_ID_JOYPAD_R3 = 15;

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_get_system_info(out retro_system_info info);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_get_system_av_info(out retro_system_av_info info);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_set_environment(retro_environment_t cb);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_set_video_refresh(retro_video_refresh_t cb);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_set_audio_sample(retro_audio_sample_t cb);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_set_audio_sample_batch(retro_audio_sample_batch_t cb);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_set_input_poll(retro_input_poll_t cb);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_set_input_state(retro_input_state_t cb);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_init();

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_deinit();

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool retro_load_game(ref retro_game_info game);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void retro_run();

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr retro_get_memory_data(uint id);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr retro_get_memory_size(uint id);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr retro_serialize_size();

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool retro_serialize(IntPtr data, IntPtr size);

        [DllImport(LibraryFileName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool retro_unserialize(IntPtr data, IntPtr size);
    }
}
