using System.Reflection;

namespace Nyra.Internal;

/// <summary>Client identification metadata: the User-Agent / Client-Name header value.</summary>
internal static class ClientInfo
{
    // NAME/VERSION format, recommended by Lavalink and required by NodeLink.
    public static string UserAgent { get; } =
        $"Nyra/{typeof(ClientInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0"}";
}
