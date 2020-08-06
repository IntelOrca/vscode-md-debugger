using System.IO.Pipes;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace IntelOrca.MegaDrive.Client
{
    public static class MegaDriveClient
    {
        public const string PIPE_DEFAULT_NAME = "IntelOrca.MegaDrive.Client.Pipe";

        public const int BUTTON_UP = 1 << 0;
        public const int BUTTON_DOWN = 1 << 1;
        public const int BUTTON_LEFT = 1 << 2;
        public const int BUTTON_RIGHT = 1 << 3;
        public const int BUTTON_B = 1 << 4;
        public const int BUTTON_C = 1 << 5;
        public const int BUTTON_A = 1 << 6;
        public const int BUTTON_START = 1 << 7;

        public static async Task<IMegaDriveClient> CreateAsync()
        {
            var stream = new NamedPipeClientStream(".", PIPE_DEFAULT_NAME, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stream.ConnectAsync();
            return JsonRpc.Attach<IMegaDriveClient>(stream);
        }
    }
}
