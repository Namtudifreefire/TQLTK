using System;
using System.IO;

namespace PersonalInfoManager.Services
{
    internal static class ApplicationDataPath
    {
        public static string Root
        {
            get
            {
#if SECURITY_TEST
                string? testRoot = Environment.GetEnvironmentVariable("TQLTK_SECURITY_TEST_ROOT");
                if (!string.IsNullOrWhiteSpace(testRoot))
                {
                    string fullTestRoot = Path.GetFullPath(testRoot);
                    string tempRoot = Path.GetFullPath(Path.GetTempPath())
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!fullTestRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Security test data must stay under the system temp directory.");
                    return fullTestRoot;
                }
#endif
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
        }
    }
}
