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

    /// <summary>Default API base URL when a platform does not override it (trailing slash required).</summary>
    public const string DefaultApiBaseAddress = "http://localhost:5000/";

    /// <summary>Windows desktop — API on the same machine.</summary>
    public const string WindowsApiBaseAddress = DefaultApiBaseAddress;

    /// <summary>Android emulator — 10.0.2.2 is the host loopback interface.</summary>
    public const string AndroidEmulatorApiBaseAddress = "http://10.0.2.2:5000/";
}
