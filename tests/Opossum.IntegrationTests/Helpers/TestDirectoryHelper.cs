namespace Opossum.IntegrationTests.Helpers;

/// <summary>
/// Utility for cleaning up temp directories in tests, including ones that contain
/// read-only files created by write-protection features.
/// </summary>
internal static class TestDirectoryHelper
{
    /// <summary>
    /// Recursively removes the read-only attribute from all files, then deletes the directory.
    /// Safe to call when the directory does not exist.
    /// </summary>
    internal static void ForceDelete(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(path, recursive: true);
    }
}
