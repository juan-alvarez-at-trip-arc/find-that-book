namespace FindThatBook.Tests.Support;

public static class RepoFiles
{
    public static string Path(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir.FullName, relative);
    }

    public static string[] Lines(string relative) => File.ReadAllLines(Path(relative));

    public static string Text(string relative) => File.ReadAllText(Path(relative));
}
