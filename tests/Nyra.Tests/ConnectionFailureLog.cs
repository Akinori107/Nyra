using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Nyra.Exceptions;
using Nyra.Internal;
using Nyra.WebSocket;

namespace Nyra.Tests;

public sealed class ConnectionFailureLog
{
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, exception));
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("logger down");
    }

    private static readonly Endpoint Target = new("127.0.0.1", 2333, "p");

    private static WebSocketException HandshakeRejected(int statusCode)
    {
        var exception = new WebSocketException("handshake rejected");
        exception.Data[WebSocketClient.HandshakeStatusCodeKey] = statusCode;
        return exception;
    }

    private static TestDataRow<Func<Exception>> Case(Func<Exception> exception, string name) =>
        new(exception, DisplayName: name);

    public static IEnumerable<TestDataRow<Func<Exception>>> ExpectedFailures()
    {
        yield return Case(
            () => new HttpRequestException("tls", new AuthenticationException()),
            "a tls failure"
        );
        yield return Case(
            () =>
                new WebSocketException(
                    "tls",
                    new HttpRequestException("tls", new AuthenticationException())
                ),
            "a tls failure in the upgrade"
        );
        yield return Case(() => new TimeoutException("slow"), "a timeout");
        yield return Case(
            () => new TaskCanceledException("slow", new TimeoutException()),
            "a cancellation caused by a timeout"
        );
        yield return Case(
            () =>
                new HttpRequestException(
                    "refused",
                    new SocketException((int)SocketError.ConnectionRefused)
                ),
            "a refused connection"
        );
        yield return Case(
            () =>
                new NodeUnreachableException(
                    "unreachable",
                    new SocketException((int)SocketError.HostNotFound)
                ),
            "an unreachable host"
        );
        yield return Case(
            () => new WebSocketException("upgrade", new HttpRequestException("upgrade")),
            "a failed upgrade request"
        );
        yield return Case(() => new RestException(401, "/v4/info", null), "a wrong password");
        yield return Case(() => HandshakeRejected(403), "a forbidden upgrade");
        yield return Case(() => HandshakeRejected(400), "a rejected user id");
        yield return Case(
            () =>
                new RestException(400, "/v4/info", "The plain HTTP request was sent to HTTPS port"),
            "plain http on the https port"
        );
        yield return Case(
            () => new RestException(524, "/v4/info", null),
            "a reverse proxy timeout"
        );
        yield return Case(() => new RestException(404, "/v4/info", null), "any other rest failure");
        yield return Case(() => new IncompatibleServerException("old"), "an incompatible server");
        yield return Case(() => new ArgumentException("bad header"), "a bad header value");
    }

    [Test]
    [MethodDataSource(nameof(ExpectedFailures))]
    public async Task AnExpectedFailureIsAWarningWithoutTheStackTrace(Exception failure)
    {
        var logger = new RecordingLogger();

        ConnectionFailure.Log(logger, "main", Target, initial: true, failure);

        await Assert.That(logger.Entries).IsEquivalentTo([(LogLevel.Warning, (Exception?)null)]);
    }

    [Test]
    public async Task ACancelledAttemptIsOnlyInformation()
    {
        var logger = new RecordingLogger();

        ConnectionFailure.Log(
            logger,
            "main",
            Target,
            initial: false,
            new OperationCanceledException()
        );

        await Assert
            .That(logger.Entries)
            .IsEquivalentTo([(LogLevel.Information, (Exception?)null)]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AnUnexpectedFailureIsAnErrorWithTheException(bool initial)
    {
        var logger = new RecordingLogger();
        var failure = new InvalidOperationException("bug");

        ConnectionFailure.Log(logger, "main", Target, initial, failure);

        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(logger.Entries[0].Exception).IsSameReferenceAs(failure);
    }

    [Test]
    public async Task AThrowingLoggerIsSwallowed()
    {
        await Assert
            .That(() =>
                ConnectionFailure.Log(
                    new ThrowingLogger(),
                    "main",
                    Target,
                    initial: true,
                    new InvalidOperationException("bug")
                )
            )
            .ThrowsNothing();
    }
}
