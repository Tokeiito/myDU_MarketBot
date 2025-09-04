using System.Threading.Tasks;

namespace MarketBot.Interfaces
{
    // Interface responsible for retrieving the price of an item
    public interface IPriceService
    {
        Task<long> GetPrice(ulong itemId, ulong marketId);
    }
}
