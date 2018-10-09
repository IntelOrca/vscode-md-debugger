namespace IntelOrca.MegaDrive.Debugger
{
    internal struct DebugMapping
    {
        public string Path { get; }
        public int Line { get; }
        public uint Address { get; }

        public DebugMapping(string path, int line, uint address)
        {
            Path = path;
            Line = line;
            Address = address;
        }

        public override string ToString()
        {
            return $"{Path}:{Line} -> {Address:X8}";
        }
    }
}
