namespace ColdCeph.Debug.Shared;

public static class RepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "coldceph.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Run cc-debug from the ColdCeph repository.");
    }
}
