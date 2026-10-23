using System.Runtime.CompilerServices;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra;

/// <summary>The audio filters of a guild connection.</summary>
public sealed class GuildFilters
{
    private static readonly JsonSerializerOptions NormalizeOptions = NyraJson.OptionsFor(false);

    private static readonly Dictionary<Type, Func<FilterChain, IFilter?, FilterChain>> FilterSlots =
        new()
        {
            [typeof(KaraokeFilter)] = (chain, filter) =>
                chain with
                {
                    Karaoke = (KaraokeFilter?)filter,
                },
            [typeof(TimescaleFilter)] = (chain, filter) =>
                chain with
                {
                    Timescale = (TimescaleFilter?)filter,
                },
            [typeof(TremoloFilter)] = (chain, filter) =>
                chain with
                {
                    Tremolo = (TremoloFilter?)filter,
                },
            [typeof(VibratoFilter)] = (chain, filter) =>
                chain with
                {
                    Vibrato = (VibratoFilter?)filter,
                },
            [typeof(DistortionFilter)] = (chain, filter) =>
                chain with
                {
                    Distortion = (DistortionFilter?)filter,
                },
            [typeof(RotationFilter)] = (chain, filter) =>
                chain with
                {
                    Rotation = (RotationFilter?)filter,
                },
            [typeof(ChannelMixFilter)] = (chain, filter) =>
                chain with
                {
                    ChannelMix = (ChannelMixFilter?)filter,
                },
            [typeof(LowPassFilter)] = (chain, filter) =>
                chain with
                {
                    LowPass = (LowPassFilter?)filter,
                },
            [typeof(EchoFilter)] = (chain, filter) => chain with { Echo = (EchoFilter?)filter },
            [typeof(ReverbFilter)] = (chain, filter) =>
                chain with
                {
                    Reverb = (ReverbFilter?)filter,
                },
            [typeof(HighPassFilter)] = (chain, filter) =>
                chain with
                {
                    HighPass = (HighPassFilter?)filter,
                },
            [typeof(ChorusFilter)] = (chain, filter) =>
                chain with
                {
                    Chorus = (ChorusFilter?)filter,
                },
            [typeof(PhaserFilter)] = (chain, filter) =>
                chain with
                {
                    Phaser = (PhaserFilter?)filter,
                },
            [typeof(FlangerFilter)] = (chain, filter) =>
                chain with
                {
                    Flanger = (FlangerFilter?)filter,
                },
            [typeof(SpatialFilter)] = (chain, filter) =>
                chain with
                {
                    Spatial = (SpatialFilter?)filter,
                },
            [typeof(CompressorFilter)] = (chain, filter) =>
                chain with
                {
                    Compressor = (CompressorFilter?)filter,
                },
            [typeof(PhonographFilter)] = (chain, filter) =>
                chain with
                {
                    Phonograph = (PhonographFilter?)filter,
                },
            [typeof(TesseractFilter)] = (chain, filter) =>
                chain with
                {
                    Tesseract = (TesseractFilter?)filter,
                },
        };

    private readonly GuildConnection _connection;
    private readonly Node _node;
    private readonly SemaphoreSlim _filterGate = new(1, 1);

    private readonly CancellationTokenSource _filterSendCts = new();
    private FilterChain _filters = new();
    private FilterChain? _pendingFilters;

    internal GuildFilters(GuildConnection connection)
    {
        _connection = connection;
        _node = connection.Node;
    }

    private ulong GuildId => _connection.GuildId;

    /// <summary>The filter chain as last written or refreshed.</summary>
    /// <remarks>
    /// An out-of-band change by a co-client only reaches this surface through the next refresh, the server
    /// confirms those through the websocket event, which this surface does not ingest. A filter write does not
    /// pick the change up either, it re-sends from Nyra's own view and replaces the co-client's change.
    /// </remarks>
    public FilterChain Applied => _filters;

    internal bool HasPendingFilters => _pendingFilters is not null;

    internal async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        _filterSendCts.Cancel();
        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _filterGate.Release();
    }

    /// <summary>Applies an audio filter, replacing the previous filter of the same kind.</summary>
    /// <param name="filter">The filter to apply.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the filter was applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="filter"/> is a user-defined implementation of <see cref="IFilter"/>, the supported set
    /// is limited to this library's sealed filter records.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A member of the filter is outside the bounds the record documents, is not a finite number, or a no-op
    /// form (every gating member null) of the rate-guarded filters (echo, chorus, phaser, flanger) was
    /// sent.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink, for the filter kinds and transitions it alone supports, or the filter is
    /// disabled in the server's config.
    /// </exception>
    public async Task AddAsync(IFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!FilterSlots.TryGetValue(filter.GetType(), out var place))
            throw new ArgumentException(
                $"The filter type '{filter.GetType().Name}' is not supported.",
                nameof(filter)
            );
        RequireNodeLinkFilters(place(new FilterChain(), filter));

        await WriteAsync(
                () =>
                {
                    var chain = place(DesiredFilters(), filter);
                    FilterValidation.ValidateFilterBounds(filter, _node.IsNodeLink);
                    return chain;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Removes the filter of the given kind from the chain, keeping the rest.</summary>
    /// <typeparam name="TFilter">The kind of filter to remove.</typeparam>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the filter was removed.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TFilter"/> is a user-defined implementation of <see cref="IFilter"/>.
    /// </exception>
    public async Task RemoveAsync<TFilter>(CancellationToken cancellationToken = default)
        where TFilter : IFilter
    {
        await RemoveAsync(typeof(TFilter), null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the audio filter of the same kind as the given one, matching is by filter type, not by value.
    /// </summary>
    /// <param name="filter">The filter of the chain to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the filter was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="filter"/> is a user-defined implementation of <see cref="IFilter"/>.
    /// </exception>
    public async Task RemoveAsync(IFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await RemoveAsync(filter.GetType(), nameof(filter), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes all audio filters, including the volume filter, the equalizer and plugin filters.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the chain was cleared.</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(() => new FilterChain(), cancellationToken);

    /// <summary>Sets the equalizer bands, replacing the previous ones, an empty list clears the equalizer.</summary>
    /// <param name="bands">
    /// The bands to apply, null clears the equalizer. Band ids are map keys, a list carrying the same id twice
    /// sends both entries, the server collapses them, the last one wins.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the equalizer was applied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A band is outside the bounds <see cref="EqualizerBand"/> documents.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink, for the transitions it alone supports, or the equalizer is disabled in the
    /// server's config.
    /// </exception>
    public Task SetEqualizerAsync(
        IReadOnlyList<EqualizerBand>? bands = null,
        CancellationToken cancellationToken = default
    ) =>
        WriteAsync(
            () => DesiredFilters() with { Equalizer = bands is { Count: > 0 } ? bands : null },
            cancellationToken
        );

    /// <summary>
    /// Sets the volume filter (0.0-5.0, 1.0 disables it), independent of the player volume, above 1.0 may
    /// clip.
    /// </summary>
    /// <remarks>Lavalink only.</remarks>
    /// <param name="volume">How loud the filter plays the track, 1.0 leaves it unchanged.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the volume filter was set.</returns>
    /// <exception cref="NotSupportedException">
    /// The node is a NodeLink server, or the volume filter is disabled in the server's config.
    /// </exception>
    public async Task SetVolumeFilterAsync(
        float volume,
        CancellationToken cancellationToken = default
    )
    {
        NodeGuards.RequireLavalink(_node, "volume filter");
        FilterValidation.ThrowIfOutOfRange(volume, 0.0f, 5.0f, "volume filter");

        await WriteAsync(() => DesiredFilters() with { Volume = volume }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a complete filter chain, replacing everything applied so far, filters left null are cleared.
    /// </summary>
    /// <param name="filters">The full chain of filters to apply.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the chain was applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A member of the chain is outside the bounds the record documents, is not a finite number, or a no-op
    /// form (every gating member null) was sent.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The node is not NodeLink, for the filter kinds and transitions it alone supports, or a member of the
    /// chain is disabled in the server's config.
    /// </exception>
    public async Task ApplyAsync(FilterChain filters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        RequireNodeLinkFilters(filters);

        await WriteAsync(
                () =>
                {
                    FilterValidation.ValidateFilterBounds(filters, _filters, _node.IsNodeLink);

                    if (filters.Volume is not null && filters.Volume != _filters.Volume)
                    {
                        NodeGuards.RequireLavalink(_node, "volume filter");
                        FilterValidation.ThrowIfOutOfRange(
                            filters.Volume,
                            0.0f,
                            5.0f,
                            "volume filter"
                        );
                    }

                    return filters;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private FilterChain DesiredFilters() => _pendingFilters ?? _filters;

    private async Task WriteAsync(Func<FilterChain> compose, CancellationToken cancellationToken)
    {
        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _connection.EnsureActive();

            await SendFiltersAsync(compose(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _filterGate.Release();
        }
    }

    private static long _ticket;

    private long _lastWriteTicket;

    private long _lastCaptureTicket;

    private const int MaxPendingFilterDivergences = 3;

    private int _pendingFilterDivergences;

    internal static long NextTicket() => Interlocked.Increment(ref _ticket);

    private void RequireNodeLinkFilters(FilterChain filters)
    {
        if (filters.Tesseract is not null)
            NodeGuards.RequireNodeLink(_node, "tesseract filter");
        else if (
            filters.Echo is not null
            || filters.Reverb is not null
            || filters.HighPass is not null
            || filters.Chorus is not null
            || filters.Phaser is not null
            || filters.Flanger is not null
            || filters.Spatial is not null
            || filters.Compressor is not null
            || filters.Phonograph is not null
        )
            NodeGuards.RequireNodeLink(_node, "this audio filter");
        else if (
            filters.Timescale is { Transition: not null }
            || filters.Tremolo is { Transition: not null }
            || filters.Vibrato is { Transition: not null }
            || filters.Karaoke is { Transition: not null }
            || filters.Distortion is { Transition: not null }
            || filters.Rotation is { Transition: not null }
            || filters.ChannelMix is { Transition: not null }
            || filters.LowPass is { Transition: not null }
            || filters.EqualizerTransition is not null
        )
            NodeGuards.RequireNodeLink(_node, "this filter transition");
    }

    private async Task RemoveAsync(
        Type filterType,
        string? paramName,
        CancellationToken cancellationToken
    )
    {
        if (!FilterSlots.TryGetValue(filterType, out var place))
            throw new ArgumentException(
                $"The filter type '{filterType.Name}' is not supported.",
                paramName
            );
        await WriteAsync(() => place(DesiredFilters(), null), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendFiltersAsync(FilterChain raw, CancellationToken cancellationToken)
    {
        ThrowIfConfigDisabled(raw, DesiredFilters());

        var desired = Normalize(raw);

        if (_connection.IsTearingDown)
            throw new InvalidOperationException(
                "The connection is being torn down, it no longer accepts operations."
            );

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _filterSendCts.Token
            );
            await PatchAndLandAsync(desired, countDivergence: false, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_filterSendCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested
            )
        {
            throw new OperationCanceledException(
                "The connection's teardown aborted this filter update.",
                cancellationToken
            );
        }
    }

    private async Task<bool> PatchAndLandAsync(
        FilterChain desired,
        bool countDivergence,
        CancellationToken cancellationToken
    )
    {
        TimescaleFilter? oldTimescale;
        lock (_connection.PlaybackPositionLock)
            oldTimescale = _filters.Timescale;

        var player = await _node
            .Rest.Players.UpdateAsync(
                GuildId,
                new PlayerUpdatePayload { Filters = desired },
                cancellationToken
            )
            .ConfigureAwait(false);

        return Land(player, desired, oldTimescale, countDivergence);
    }

    private bool Land(
        Player player,
        FilterChain desired,
        TimescaleFilter? oldTimescale,
        bool countDivergence
    )
    {
        var stillPending =
            _node.IsNodeLink
            && player.Filters is { } echoed
            && !FilterComparer.FiltersEqual(echoed, desired);

        bool gaveUp;
        lock (_connection.PlaybackPositionLock)
        {
            _filters =
                _node.IsNodeLink && player.Filters is { } effective
                    ? effective
                    : StripConfigDisabledFilters(desired);
            gaveUp = CommitPendingUnderLock(desired, stillPending, countDivergence);

            ReanchorIfPaceChanged(oldTimescale);
        }

        _lastWriteTicket = NextTicket();
        return gaveUp;
    }

    private void ReanchorIfPaceChanged(TimescaleFilter? oldTimescale)
    {
        var applied = _filters.Timescale;
        if (
            (oldTimescale?.Speed ?? 1.0) == (applied?.Speed ?? 1.0)
            && (oldTimescale?.Rate ?? 1.0) == (applied?.Rate ?? 1.0)
        )
            return;

        _connection.ReanchorTimescaleUnderLock(oldTimescale);
    }

    private static readonly TimeSpan ResendDeadline = TimeSpan.FromSeconds(10);

    private readonly StrongBox<long> _resendNotBefore = new();

    internal async Task ResendPendingFiltersAsync()
    {
        if (Environment.TickCount64 < Volatile.Read(ref _resendNotBefore.Value))
            return;

        await _filterGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (
                _pendingFilters is not { } desired
                || !_node.IsReady
                || !_connection.IsConnected
                || _connection.IsTearingDown
            )
                return;

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(
                _filterSendCts.Token
            );
            attemptCts.CancelAfter(ResendDeadline);
            if (
                await PatchAndLandAsync(desired, countDivergence: true, attemptCts.Token)
                    .ConfigureAwait(false)
            )
                _node.HandleError(
                    new InvalidOperationException(
                        $"The server of guild {GuildId} keeps echoing a different filter chain after {MaxPendingFilterDivergences} attempts, giving up re-sending it."
                    )
                );
        }
        catch (OperationCanceledException) when (_filterSendCts.IsCancellationRequested) { }
        catch (RestException restFailure) when (restFailure.RetryAfter is { } retryAfter)
        {
            Volatile.Write(
                ref _resendNotBefore.Value,
                Environment.TickCount64 + (long)retryAfter.TotalMilliseconds
            );
            _node.HandleError(
                new InvalidOperationException(
                    $"The server of guild {GuildId} answered the filter re-send with 429, waiting the retry-after before the next attempt.",
                    restFailure
                )
            );
        }
        catch (Exception exception)
        {
            bool gaveUp;
            lock (_connection.PlaybackPositionLock)
                gaveUp = CountFailedResendUnderLock();

            _node.HandleError(
                gaveUp
                    ? new InvalidOperationException(
                        $"The server of guild {GuildId} keeps failing to re-apply its filters after {MaxPendingFilterDivergences} attempts, giving up re-sending them.",
                        exception
                    )
                    : new InvalidOperationException(
                        $"Failed to re-apply the filters of guild {GuildId}: {exception.Message}",
                        exception
                    )
            );
        }
        finally
        {
            _filterGate.Release();
        }
    }

    private bool CountFailedResendUnderLock()
    {
        return _pendingFilters is not null && GiveUpAfterDivergenceUnderLock();
    }

    private bool GiveUpAfterDivergenceUnderLock()
    {
        _pendingFilterDivergences++;
        if (_pendingFilterDivergences < MaxPendingFilterDivergences)
            return false;

        _pendingFilters = null;
        _pendingFilterDivergences = 0;
        return true;
    }

    internal async Task CaptureServerFiltersAsync(
        Player player,
        long requestTicket,
        CancellationToken cancellationToken
    )
    {
        await _filterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection.IsTearingDown)
                return;

            if (requestTicket < _lastWriteTicket || requestTicket < _lastCaptureTicket)
                return;
            _lastCaptureTicket = requestTicket;

            if (player.Filters is { } serverFilters)
            {
                TimescaleFilter? oldTimescale;
                lock (_connection.PlaybackPositionLock)
                {
                    oldTimescale = _filters.Timescale;
                    _filters = serverFilters;
                    ReanchorIfPaceChanged(oldTimescale);
                }
            }
        }
        finally
        {
            _filterGate.Release();
        }
    }

    internal async Task ResetAsync()
    {
        _filterSendCts.Cancel();
        await _filterGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _pendingFilters = null;
            _filters = new();
        }
        finally
        {
            _filterGate.Release();
        }
    }

    private bool CommitPendingUnderLock(
        FilterChain desired,
        bool stillPending,
        bool countDivergence
    )
    {
        if (!stillPending)
        {
            _pendingFilters = null;
            _pendingFilterDivergences = 0;
            return false;
        }

        if (countDivergence)
        {
            if (GiveUpAfterDivergenceUnderLock())
                return true;
        }
        else
            _pendingFilterDivergences = 0;

        _pendingFilters = desired;
        return false;
    }

    private void ThrowIfConfigDisabled(FilterChain chain, FilterChain landed)
    {
        if (_node.ServerInfo is not { IsNodeLink: false, EnabledFilters: { } enabled })
            return;

        Require(chain.Volume is not null && chain.Volume != landed.Volume, "volume");
        Require(
            FilterComparer.Touched(chain.Equalizer, landed.Equalizer)
                || FilterComparer.Touched(chain.EqualizerTransition, landed.EqualizerTransition),
            "equalizer"
        );
        Require(FilterComparer.Touched(chain.Karaoke, landed.Karaoke), "karaoke");
        Require(FilterComparer.Touched(chain.Timescale, landed.Timescale), "timescale");
        Require(FilterComparer.Touched(chain.Tremolo, landed.Tremolo), "tremolo");
        Require(FilterComparer.Touched(chain.Vibrato, landed.Vibrato), "vibrato");
        Require(FilterComparer.Touched(chain.Distortion, landed.Distortion), "distortion");
        Require(FilterComparer.Touched(chain.Rotation, landed.Rotation), "rotation");
        Require(FilterComparer.Touched(chain.ChannelMix, landed.ChannelMix), "channelMix");
        Require(FilterComparer.Touched(chain.LowPass, landed.LowPass), "lowPass");

        void Require(bool touched, string kind)
        {
            if (touched && !enabled.Contains(kind))
                throw DisabledFilter(kind);
        }
    }

    private static NotSupportedException DisabledFilter(string kind) =>
        new(
            $"The {kind} filter is disabled in this server's config, it cannot be applied. Omit it to continue."
        );

    private FilterChain StripConfigDisabledFilters(FilterChain chain)
    {
        if (_node.ServerInfo is not { IsNodeLink: false, EnabledFilters: { } enabled })
            return chain;

        return chain with
        {
            Volume = enabled.Contains("volume") ? chain.Volume : null,
            Equalizer = enabled.Contains("equalizer") ? chain.Equalizer : null,
            Karaoke = enabled.Contains("karaoke") ? chain.Karaoke : null,
            Timescale = enabled.Contains("timescale") ? chain.Timescale : null,
            Tremolo = enabled.Contains("tremolo") ? chain.Tremolo : null,
            Vibrato = enabled.Contains("vibrato") ? chain.Vibrato : null,
            Distortion = enabled.Contains("distortion") ? chain.Distortion : null,
            Rotation = enabled.Contains("rotation") ? chain.Rotation : null,
            ChannelMix = enabled.Contains("channelMix") ? chain.ChannelMix : null,
            LowPass = enabled.Contains("lowPass") ? chain.LowPass : null,
        };
    }

    private static FilterChain Normalize(FilterChain chain) =>
        NyraJson.Deserialize<FilterChain>(
            NyraJson.SerializeToUtf8Bytes(chain, NormalizeOptions),
            NormalizeOptions
        )
            is { } normalized
            ? (
                chain.EqualizerTransition is null
                    ? normalized
                    : normalized with
                    {
                        EqualizerTransition = chain.EqualizerTransition,
                    }
            )
            : new FilterChain();
}
