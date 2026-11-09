namespace PersonalInfoManager.Services;

internal static class UnsupportedStorage
{
    internal static int Calls { get; private set; }
    internal static Exception Block()
    {
        Calls++;
        return new PlatformNotSupportedException("Local storage/DPAPI is outside the Linux inspection tests.");
    }
}

internal static class LocalizationAuditService
{
    public static string Get(string key, string language) => key;
}

internal sealed class LocalFileService
{
    public List<FileInfo> ListFiles(string username) => throw UnsupportedStorage.Block();
    public long GetPlaintextLength(string path) => throw UnsupportedStorage.Block();
    public void CopyToPlaintext(string path, Stream destination) => throw UnsupportedStorage.Block();
    public string GetExistingUserFolderReadOnly(string username) => throw UnsupportedStorage.Block();
    public string GetUserFolder(string username) => throw UnsupportedStorage.Block();
    public bool ContentEqualsReadOnly(string path, Stream content, long length, CancellationToken cancellationToken = default)
        => throw UnsupportedStorage.Block();
    public bool ContentEquals(string path, Stream content, long length, CancellationToken cancellationToken = default)
        => throw UnsupportedStorage.Block();
    public string ImportStream(string username, string name, Stream content, long length,
        DateTime? lastWriteTimeUtc = null, CancellationToken cancellationToken = default)
        => throw UnsupportedStorage.Block();
}

internal static class LocalFileEncryptionService
{
    public static void EncryptStreamAtomic(Stream content, long length, string path, bool overwrite,
        DateTime? lastWriteTimeUtc = null, CancellationToken cancellationToken = default)
        => throw UnsupportedStorage.Block();
    public static long GetPlaintextLength(string path) => throw UnsupportedStorage.Block();
    public static bool ContentEqualsStored(string path, Stream content, long length, CancellationToken cancellationToken = default)
        => throw UnsupportedStorage.Block();
}
