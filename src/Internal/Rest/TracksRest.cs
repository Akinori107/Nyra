using System.Globalization;
using System.Net;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class TracksRest
{
    private readonly RestClient _rest;

    internal TracksRest(RestClient rest) => _rest = rest;

    public async Task<LoadResult> LoadAsync(string identifier, CancellationToken cancellationToken)
    {
        var path = string.Concat(
            Routes.LoadTracks,
            "?identifier=",
            Uri.EscapeDataString(identifier)
        );
        return await _rest
            .SendExpectingAsync<LoadResult>(
                HttpMethod.Get,
                path,
                "load result",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<Track> DecodeAsync(string encodedTrack, CancellationToken cancellationToken)
    {
        var path = string.Concat(
            Routes.DecodeTrack,
            "?encodedTrack=",
            Uri.EscapeDataString(encodedTrack)
        );
        return await _rest
            .SendExpectingAsync<Track>(
                HttpMethod.Get,
                path,
                "track",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Track>> DecodeManyAsync(
        IReadOnlyList<string> encodedTracks,
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(encodedTracks, options);
        var json = await _rest
            .SendAsync(HttpMethod.Post, Routes.DecodeTracks, body, cancellationToken)
            .ConfigureAwait(false);
        return _rest.DeserializeList<Track>(json, Routes.DecodeTracks, options);
    }

    public async Task<string> EncodeAsync(
        TrackEncodeRequest info,
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(new[] { info }, options);
        var json = await _rest
            .SendAsync(HttpMethod.Post, Routes.EncodedTracks, body, cancellationToken)
            .ConfigureAwait(false);
        var encoded = _rest.DeserializeList<string>(json, Routes.EncodedTracks, options);
        return encoded is { Count: > 0 }
            ? encoded[0]
            : throw new RestException(
                (int)HttpStatusCode.OK,
                Routes.EncodedTracks,
                "The server returned no encoded track."
            );
    }

    public async Task<IReadOnlyList<string>> EncodeManyAsync(
        IReadOnlyList<TrackEncodeRequest> infos,
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(infos, options);
        var json = await _rest
            .SendAsync(HttpMethod.Post, Routes.EncodedTracks, body, cancellationToken)
            .ConfigureAwait(false);
        return _rest.DeserializeList<string>(json, Routes.EncodedTracks, options);
    }

    public async Task<TrackStreamUrl> GetStreamUrlAsync(
        string encodedTrack,
        int? itag,
        CancellationToken cancellationToken
    )
    {
        var path = string.Concat(
            Routes.TrackStream,
            "?encodedTrack=",
            Uri.EscapeDataString(encodedTrack)
        );
        if (itag is not null)
            path = string.Concat(path, "&itag=", itag.Value.ToString(CultureInfo.InvariantCulture));
        var result = await _rest
            .SendExpectingAsync<TrackStreamUrl>(
                HttpMethod.Get,
                path,
                "track stream response",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.Url))
            throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned no stream URL."
            );
        return result;
    }

    public async Task<Stream> OpenLoadStreamAsync(
        string encodedTrack,
        int volume,
        long positionMilliseconds,
        FilterChain? filters,
        LoadStreamFormat format,
        CancellationToken cancellationToken
    )
    {
        var path = string.Concat(
            Routes.LoadStream,
            "?encodedTrack=",
            Uri.EscapeDataString(encodedTrack),
            "&volume=",
            volume.ToString(CultureInfo.InvariantCulture),
            "&position=",
            positionMilliseconds.ToString(CultureInfo.InvariantCulture)
        );
        if (filters is not null)
            path = string.Concat(
                path,
                "&filters=",
                Uri.EscapeDataString(NyraJson.Serialize(filters, NyraJson.OptionsFor(true)))
            );
        if (format is LoadStreamFormat.Wav)
            path = string.Concat(path, "&format=wav");

        using var request = _rest.CreateRequest(HttpMethod.Get, path, "application/octet-stream");

        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerCts.CancelAfter(TimeSpan.FromSeconds(60));
        HttpResponseMessage response;
        try
        {
            response = await RestClient
                .StreamingHttp.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    headerCts.Token
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (headerCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The node '{_rest.Node.Name}' at {_rest.Node.Endpoint} did not answer the load stream headers within sixty seconds."
            );
        }
        catch (HttpRequestException exception)
        {
            throw new NodeUnreachableException(
                $"The node '{_rest.Node.Name}' at {_rest.Node.Endpoint} is not reachable: {exception.Message}",
                exception
            );
        }

        if (!response.IsSuccessStatusCode)
        {
            string body;
            TimeSpan? retryAfter;
            try
            {
                using var tailCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    headerCts.Token
                );
                body = await response
                    .Content.ReadAsStringAsync(tailCts.Token)
                    .ConfigureAwait(false);
                retryAfter = RestClient.ReadRetryAfter(response);
            }
            catch (OperationCanceledException)
                when (headerCts.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested
                )
            {
                throw new TimeoutException(
                    $"The node '{_rest.Node.Name}' at {_rest.Node.Endpoint} did not answer the load stream within sixty seconds."
                );
            }
            finally
            {
                response.Dispose();
            }

            throw new RestException((int)response.StatusCode, path, body, retryAfter);
        }

        try
        {
            using var tailCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                headerCts.Token
            );
            return await response.Content.ReadAsStreamAsync(tailCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (headerCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            response.Dispose();
            throw new TimeoutException(
                $"The node '{_rest.Node.Name}' at {_rest.Node.Endpoint} did not answer the load stream within sixty seconds."
            );
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
