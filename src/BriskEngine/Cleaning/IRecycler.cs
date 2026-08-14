namespace BriskEngine.Cleaning;

public interface IRecycler
{
    /// Sends a file or directory to the Recycle Bin (never permanent).
    void Recycle(string path);
}
