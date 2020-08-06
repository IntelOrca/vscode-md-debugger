namespace IntelOrca.MegaDrive.Host
{
    public interface ISram
    {
        void Load();
        void Save();
        bool Update();
    }
}
