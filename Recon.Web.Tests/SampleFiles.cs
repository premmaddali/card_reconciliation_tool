namespace Recon.Web.Tests;

internal static class SampleFiles
{
    public static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "SharedFiles");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("SharedFiles could not be found above the test output directory.");
        }
    }
}
