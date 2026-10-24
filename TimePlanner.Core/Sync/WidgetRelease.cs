namespace TimePlanner.Core.Sync;

public sealed record WidgetRelease(string? LatestVersion)
{
    // Stable release numbers only. Build metadata does not affect version ordering.
    public static Version? Parse(string? value)
    {
        var number = value?.Trim().TrimStart('v').Split('+')[0];
        return Version.TryParse(number, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision))
            : null;
    }

    public bool IsNewerThan(string current) => Parse(LatestVersion) is { } latest
        && Parse(current) is { } installed && latest > installed;
}
