using System;
using IntelOrca.MegaDrive.Debugger;

namespace mddebug
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            // System.Diagnostics.Debugger.Launch();
            var adapter = new MegaDriveDebugAdapter(Console.OpenStandardInput(), Console.OpenStandardOutput());
            adapter.Protocol.Run();
            adapter.Protocol.WaitForReader();
        }
    }
}
