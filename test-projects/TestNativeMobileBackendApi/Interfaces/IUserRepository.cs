using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Interfaces;

public interface IUserRepository
{
    AppUser? FindByUserName(string userName);
    AppUser? FindById(Guid id);
    bool UserNameExists(string userName);
    bool EmailExists(string email);
    void Insert(AppUser user);
    bool UpdateRole(Guid userId, string role);
    int CountByRole(string role);
}
