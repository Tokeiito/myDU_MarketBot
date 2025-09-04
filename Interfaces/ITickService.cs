namespace MarketBot.Interfaces
{
    public interface ITickService
    {
        void RegisterTickable(ITickable tickable);
        void Start();
        void Stop();
    }
}
