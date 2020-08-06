namespace IntelOrca.MegaDrive.Host
{
    public class SramNone : ISram
    {
        public void Load() { }
        public void Save() { }
        public bool Update() => false;
    }
}
