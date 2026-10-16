using System;
using System.IO;

namespace BriskEngine.Cleaning;

public interface IDeleter
{
    /// Deletes what it can. A file in use is left behind without throwing;
    /// the caller observes what remains.
    void Delete(string path);
}

public sealed class WindowsDeleter : IDeleter
{
    public void Delete(string path)
    {
        var full = DeleteLockProbe.ExtendedLength(path);
        if (File.Exists(full)) { DeleteFile(full); return; }
        if (Directory.Exists(full)) DeleteTree(full);
    }

    private static void DeleteFile(string file)
    {
        try
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteTree(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir)) DeleteFile(file);
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            // A junction or symlink is removed as a link; what it points to
            // is never walked.
            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
            {
                try { Directory.Delete(sub, recursive: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                continue;
            }
            DeleteTree(sub);
        }
        try { Directory.Delete(dir, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
