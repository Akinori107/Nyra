using Microsoft.Extensions.Logging;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class NodeServerInfo
{
    private const int MaxSharedProbeAttempts = 3;

    private readonly Node _node;
    private readonly Lock _probeLock = new();
    private volatile ServerInfo? _info;
    private Task? _probe;
    private int _epoch;

    internal NodeServerInfo(Node node) => _node = node;

    internal ServerInfo? Current => _info;

    internal Task EnsureAsync(CancellationToken cancellationToken, bool forceProbe = false)
    {
        if (forceProbe)
            return ProbeAsync(forceProbe: true, cancellationToken);

        if (_info is not null)
            return Task.CompletedTask;

        return EnsureSharedProbeAsync(cancellationToken);
    }

    private async Task EnsureSharedProbeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxSharedProbeAttempts && _info is null; attempt++)
        {
            Task probe;
            lock (_probeLock)
            {
                if (_probe is not { IsCompleted: false })
                {
                    _probe = ProbeAsync(forceProbe: false, CancellationToken.None);
                    _probe.ObserveFaults();
                }

                probe = _probe;
            }

            await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProbeAsync(bool forceProbe, CancellationToken cancellationToken)
    {
        var epoch = forceProbe ? Interlocked.Increment(ref _epoch) : Volatile.Read(ref _epoch);

        ServerInfo info;
        try
        {
            info = await _node
                .Rest.Info.GetServerInfoAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RestException exception) when (exception.StatusCode == 404)
        {
            throw new IncompatibleServerException(
                $"The server at {_node.Endpoint} does not expose /v4/info, only Lavalink v"
                    + NodeVersion.MinimumLavalink
                    + "+ and NodeLink v"
                    + NodeVersion.MinimumNodeLink
                    + "+ are supported.",
                exception
            );
        }

        if (string.IsNullOrEmpty(info.RawVersion))
            throw new IncompatibleServerException(
                $"The server at {_node.Endpoint} did not report a version."
            );

        if (info.Version is { } version)
        {
            var minimum = info.IsNodeLink
                ? NodeVersion.MinimumNodeLink
                : NodeVersion.MinimumLavalink;
            if (version < minimum)
            {
                var software = info.IsNodeLink ? "NodeLink" : "Lavalink";
                throw new IncompatibleServerException(
                    $"{software} {version} at {_node.Endpoint} is too old, {software} {minimum} or newer is required."
                );
            }
        }
        else if (info.RawVersion.Contains('.'))
            try
            {
                _node.Logger?.LogWarning(
                    LogEvents.Node,
                    "Node {NodeName} reported a version ('{RawVersion}') the library cannot parse, the compatibility floor was skipped. Dev builds report a commit hash instead; if this is a versioned fork, verify it meets {Minimum}.",
                    _node.Name,
                    Node.LogSafe(info.RawVersion),
                    info.IsNodeLink
                        ? NodeVersion.MinimumNodeLink.ToString()
                        : NodeVersion.MinimumLavalink.ToString()
                );
            }
            catch { }

        var published = false;
        lock (_node.EventsLock)
            if (Volatile.Read(ref _epoch) == epoch)
            {
                _info = info;
                published = true;
            }

        if (published)
            try
            {
                _node.Logger?.LogInformation(
                    LogEvents.Node,
                    "Node {NodeName} runs {Software} {Version}.",
                    _node.Name,
                    info.IsNodeLink ? "NodeLink" : "Lavalink",
                    Node.LogSafe(info.RawVersion)
                );
            }
            catch { }
    }

    internal void Invalidate()
    {
        lock (_node.EventsLock)
        {
            Interlocked.Increment(ref _epoch);
            _info = null;
        }
    }
}
