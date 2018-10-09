using System;
using System.IO;

namespace IntelOrca.MegaDrive.Host
{
    public class Sram
    {
        private readonly MegaDriveHost _host;
        private readonly string _path;
        private byte[] _cached;

        public Sram(MegaDriveHost host, string path)
        {
            _host = host;
            _path = path;
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    _cached = File.ReadAllBytes(_path);
                    var sram = _host.GetSaveRAM();
                    if (sram != null)
                    {
                        _cached.AsSpan().CopyTo(sram);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unable to load sram: {0}", ex.Message);
            }
        }

        public void Save()
        {
            if (_cached != null)
            {
                try
                {
                    EnsureDirectoryExists(Path.GetDirectoryName(_path));
                    File.WriteAllBytes(_path, _cached);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Unable to save sram: {0}", ex.Message);
                }
            }
        }

        public bool Update()
        {
            const int UNINIT_SRAM_SIZE = 0x10000;
            bool result = false;
            var sram = _host.GetSaveRAM();
            if (sram != null && sram.Length < UNINIT_SRAM_SIZE)
            {
                if (_cached == null || sram.Length != _cached.Length)
                {
                    Array.Resize(ref _cached, sram.Length);
                    sram.CopyTo(_cached.AsSpan());
                    result = true;
                }
                else
                {
                    for (int i = 0; i < sram.Length; i++)
                    {
                        if (_cached[i] != sram[i])
                        {
                            _cached[i] = sram[i];
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        private static void EnsureDirectoryExists(string path)
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists)
            {
                di.Create();
            }
        }
    }
}
