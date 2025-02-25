// NewwaysAdmin.Shared/IO/IDataStorage.cs
using System.Threading.Tasks;

namespace NewwaysAdmin.Shared.IO
{
    public interface IDataStorageBase
    {
        // Common non-generic operations if needed
    }

    public interface IDataStorage<T> : IDataStorageBase where T : class, new()
    {
        Task<T> LoadAsync(string identifier);
        Task SaveAsync(string identifier, T data);
        Task<bool> ExistsAsync(string identifier);
        Task<IEnumerable<string>> ListIdentifiersAsync();
        Task DeleteAsync(string identifier);
    }
}