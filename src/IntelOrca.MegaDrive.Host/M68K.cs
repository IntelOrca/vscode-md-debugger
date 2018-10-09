using System;
using System.Runtime.InteropServices;

namespace IntelOrca.MegaDrive.Host
{
    internal enum M68K_REG
    {
        D0,
        D1,
        D2,
        D3,
        D4,
        D5,
        D6,
        D7,
        A0,
        A1,
        A2,
        A3,
        A4,
        A5,
        A6,
        A7,
    }

    internal struct M68K
    {
        private readonly IntPtr _base;

        public M68K(IntPtr @base)
        {
            _base = @base;
        }

        public uint GetRegister(M68K_REG reg) => (uint)Marshal.ReadInt32(_base, 0x2814 + ((int)reg * 4));
        public void SetRegister(M68K_REG reg, uint value) => Marshal.WriteInt32(_base, 0x2814 + ((int)reg * 4), (int)value);
        public uint PC
        {
            get => (uint)Marshal.ReadInt32(_base, 0x2854);
            set => Marshal.WriteInt32(_base, 0x2854, (int)value);
        }
    }
}
