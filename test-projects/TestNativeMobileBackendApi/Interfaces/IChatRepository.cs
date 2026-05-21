using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Interfaces;

public interface IChatRepository
{
    IEnumerable<ChatMessage> GetRecent(int limit);
    ChatMessage Insert(Guid userId, string userName, string message);
}
