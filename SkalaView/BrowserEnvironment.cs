using System;

namespace SkalaView;

public static class BrowserEnvironment
{
    public static string? SameOriginApiBaseUrl { get; private set; }

    public static void Configure(string[] args)
    {
        if (args.Length == 0) return;
        if (!Uri.TryCreate(args[0], UriKind.Absolute, out var currentUrl)) return;

        SameOriginApiBaseUrl = new Uri(currentUrl, "/api/data").ToString();
    }
}
