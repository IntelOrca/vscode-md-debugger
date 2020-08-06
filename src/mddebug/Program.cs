using System;
using IntelOrca.MegaDrive.Debugger;
using IntelOrca.MegaDrive.Host;

namespace mddebug
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "remote")
            {
                using var rpcHost = new RpcMegaDriveHost();
                rpcHost.WaitForExit();
            }
            else
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
}
