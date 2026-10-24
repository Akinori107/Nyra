using System.Reflection;

namespace Nyra.Internal;

internal static class ClientHeaders
{
    public static string UserAgent { get; } =
        $"Nyra/{(VersionText() is { Length: > 0 } version ? version.Split('+')[0] : "0.0.0")}";

    private static string? VersionText() =>
        typeof(ClientHeaders)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
}
