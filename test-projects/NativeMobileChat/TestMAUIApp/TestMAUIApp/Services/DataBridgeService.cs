using SQLite;
using TestMAUIApp.Models;

namespace TestMAUIApp.Services;

public class DataBridgeService
{
    SQLiteAsyncConnection? _database;

    async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_database is not null)
        {
            return _database;
        }

        _database = new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags);
        await _database.CreateTableAsync<LocalUser>().ConfigureAwait(false);
        await _database.CreateTableAsync<ChatMessageRecord>().ConfigureAwait(false);
        return _database;
    }

    public async Task<List<LocalUser>> GetUsersAsync()
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<LocalUser>().ToListAsync().ConfigureAwait(false);
    }

    public async Task<LocalUser?> GetUserAsync(int id)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<LocalUser>().Where(u => u.ID == id).FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task<LocalUser?> GetUserByExternalIdAsync(string externalId)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<LocalUser>()
            .Where(u => u.ExternalId == externalId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<int> SaveUserAsync(LocalUser user)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        if (user.ID != 0)
        {
            return await database.UpdateAsync(user).ConfigureAwait(false);
        }

        return await database.InsertAsync(user).ConfigureAwait(false);
    }

    public async Task<int> DeleteUserAsync(LocalUser user)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.DeleteAsync(user).ConfigureAwait(false);
    }

    public async Task<List<ChatMessageRecord>> GetMessagesAsync(string conversationId)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<ChatMessageRecord>()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAtUtc)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task ClearMessagesAsync(string conversationId)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        var messages = await database.Table<ChatMessageRecord>()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            await database.DeleteAsync(message).ConfigureAwait(false);
        }
    }

    public async Task<int> SaveMessageAsync(ChatMessageRecord message)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        if (message.ID != 0)
        {
            return await database.UpdateAsync(message).ConfigureAwait(false);
        }

        return await database.InsertAsync(message).ConfigureAwait(false);
    }

    public async Task<int> DeleteMessageAsync(ChatMessageRecord message)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.DeleteAsync(message).ConfigureAwait(false);
    }
}
