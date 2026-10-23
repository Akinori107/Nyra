using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra;

/// <summary>The IP route planner of a node.</summary>
public sealed class NodeRoutePlanner
{
    private readonly Node _node;

    internal NodeRoutePlanner(Node node) => _node = node;

    /// <summary>
    /// Fetches the IP route planner status, the strategy, its IP block and the addresses it treats as failing.
    /// </summary>
    /// <remarks>Served by both servers.</remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status of the route planner.</returns>
    /// <exception cref="RestException">The server has no route planner configured.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task<RoutePlannerStatus> GetStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        return await _node
            .Rest.Info.GetRoutePlannerStatusAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Releases an address the route planner treats as failing back into rotation.</summary>
    /// <param name="address">The address to release, as the status reported it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the address was released.</returns>
    /// <exception cref="RestException">The server has no route planner configured, or rejected the address.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task FreeAddressAsync(
        string address,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        address = address.TrimStart('/');

        await _node
            .Rest.Info.FreeRoutePlannerAddressAsync(address, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Releases every address the route planner treats as failing back into rotation.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when every address was released.</returns>
    /// <exception cref="RestException">The server has no route planner configured.</exception>
    /// <exception cref="IncompatibleServerException">The server's /v4/info version is not supported.</exception>
    public async Task FreeAllAddressesAsync(CancellationToken cancellationToken = default)
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        await _node
            .Rest.Info.FreeRoutePlannerAddressesAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
