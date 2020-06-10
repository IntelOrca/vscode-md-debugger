using System;
using IntelOrca.MegaDrive.Debugger;

namespace mddebug
{
    internal class Program
    {
        public static void Main(string[] args)
        {
#if DEBUG
            System.Diagnostics.Debugger.Launch();
#endif
            var adapter = new MegaDriveDebugAdapter(Console.OpenStandardInput(), Console.OpenStandardOutput());
            adapter.Protocol.Run();
            adapter.Protocol.WaitForReader();
        }
    }
}
