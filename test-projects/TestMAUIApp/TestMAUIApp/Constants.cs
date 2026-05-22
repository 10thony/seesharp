using SQLite;

namespace TestMAUIApp;

public static class Constants
{
    public const string DatabaseFilename = "TestMAUIApp.db3";

    public const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache;

    public static string DatabasePath =>
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);

    /// <summary>Shared chat thread id for local SQLite cache.</summary>
    public const string GlobalConversationId = "global";

    public const string UserConversationPrefix = "user:";
    public const string GroupConversationPrefix = "group:";

    public static string UserConversationId(string userId) => $"{UserConversationPrefix}{userId}";

    public static string GroupConversationId(string groupId) => $"{GroupConversationPrefix}{groupId}";

    public static bool IsUserConversation(string conversationId) =>
        conversationId.StartsWith(UserConversationPrefix, StringComparison.Ordinal);

    public static bool IsGroupConversation(string conversationId) =>
        conversationId.StartsWith(GroupConversationPrefix, StringComparison.Ordinal);

    public static string? ExtractUserId(string conversationId) =>
        IsUserConversation(conversationId) ? conversationId[UserConversationPrefix.Length..] : null;

    public static string? ExtractGroupId(string conversationId) =>
        IsGroupConversation(conversationId) ? conversationId[GroupConversationPrefix.Length..] : null;

    /// <summary>Default API base URL when a platform does not override it (trailing slash required).</summary>
    public const string DefaultApiBaseAddress = "http://localhost:5000/";

    /// <summary>Windows desktop — API on the same machine.</summary>
    public const string WindowsApiBaseAddress = DefaultApiBaseAddress;

    /// <summary>Android emulator — 10.0.2.2 is the host loopback interface.</summary>
    public const string AndroidEmulatorApiBaseAddress = "http://10.0.2.2:5000/";
}
