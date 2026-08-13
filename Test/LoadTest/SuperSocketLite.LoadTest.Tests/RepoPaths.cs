namespace SuperSocketLite.LoadTest.Tests;

/// <summary>저장소 안의 고정 파일을 현재 디렉토리와 무관하게 찾습니다.</summary>
internal static class RepoPaths
{
    private const string RootMarker = "SuperSocketLite2.slnx";

    private static readonly Lazy<string> LazyRoot = new(FindRoot);

    /// <summary>저장소 루트의 절대 경로입니다.</summary>
    public static string Root => LazyRoot.Value;

    /// <summary>저장소 루트를 기준으로 경로를 만듭니다.</summary>
    public static string Combine(params string[] segments)
    {
        return Path.Combine(new[] { Root }.Concat(segments).ToArray());
    }

    private static string FindRoot()
    {
        // 실행 디렉토리(bin/Release/net10.0)에서 위로 올라가며 루트 표식을 찾습니다.
        // dotnet run 은 셸의 현재 디렉토리를 그대로 쓰므로 현재 디렉토리에 기대면 안 됩니다.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RootMarker)))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root ('{RootMarker}') above '{AppContext.BaseDirectory}'.");
    }
}
