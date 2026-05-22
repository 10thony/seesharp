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
        await _database.CreateTableAsync<DirectThreadRecord>().ConfigureAwait(false);
        await _database.CreateTableAsync<ChatGroupRecord>().ConfigureAwait(false);
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

    public async Task<List<DirectThreadRecord>> GetDirectThreadsAsync()
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<DirectThreadRecord>()
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<DirectThreadRecord?> GetDirectThreadAsync(string recipientId)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<DirectThreadRecord>()
            .Where(t => t.RecipientId == recipientId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task SaveDirectThreadAsync(DirectThreadRecord thread)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        var existing = await GetDirectThreadAsync(thread.RecipientId).ConfigureAwait(false);
        if (existing is null)
        {
            await database.InsertAsync(thread).ConfigureAwait(false);
            return;
        }

        existing.RecipientName = thread.RecipientName;
        await database.UpdateAsync(existing).ConfigureAwait(false);
    }

    public async Task<List<ChatGroupRecord>> GetGroupsAsync()
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<ChatGroupRecord>()
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ChatGroupRecord?> GetGroupAsync(string groupId)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        return await database.Table<ChatGroupRecord>()
            .Where(g => g.GroupId == groupId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task SaveGroupAsync(ChatGroupRecord group)
    {
        var database = await GetDatabaseAsync().ConfigureAwait(false);
        var existing = await GetGroupAsync(group.GroupId).ConfigureAwait(false);
        if (existing is null)
        {
            await database.InsertAsync(group).ConfigureAwait(false);
            return;
        }

        existing.Name = group.Name;
        existing.MemberIds = group.MemberIds;
        await database.UpdateAsync(existing).ConfigureAwait(false);
    }
}
