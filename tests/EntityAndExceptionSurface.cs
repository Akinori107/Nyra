using Nyra.Abstractions;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class EntityAndExceptionSurface
{
    private static T? Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"the expected {typeof(T).Name} never came");
    }

    [Test]
    public async Task Endpoint()
    {
        var ipv4 = new Endpoint("127.0.0.1", 3000, "pass");
        await Assert.That(ipv4.Host).IsEqualTo("127.0.0.1");
        var bracketedShort = new Endpoint("[::1]", 3000, "pass");
        await Assert.That(bracketedShort.Host).IsEqualTo("[::1]");
        var bracketedFull = new Endpoint("[0000:0000:0000:0000:0000:0000:0000:0001]", 3000, "pass");
        await Assert
            .That(bracketedFull.Host)
            .IsEqualTo("[0000:0000:0000:0000:0000:0000:0000:0001]");
        var host = new Endpoint("example.com", 3000, "pass");
        await Assert.That(host.Host).IsEqualTo("example.com");

        Throws<ArgumentException>(() => _ = new Endpoint("a b", 1, "p"));
        Throws<ArgumentException>(() => _ = new Endpoint("http://x", 1, "p"));
        Throws<ArgumentException>(() => _ = new Endpoint("host:1", 1, "p"));
        using (Assert.Multiple())
        {
            await Assert
                .That(new Endpoint("192.168.001.1", 1, "p").Host)
                .IsEqualTo("192.168.001.1");
            await Assert.That(new Endpoint("0.0.0.0", 1, "p").Host).IsEqualTo("0.0.0.0");
            await Assert.That(new Endpoint("[::]", 1, "p").Host).IsEqualTo("[::]");
            await Assert.That(new Endpoint("[not-an-ip]", 1, "p").Host).IsEqualTo("[not-an-ip]");
            await Assert
                .That(new Endpoint("[fe80::1%zone]", 1, "p").Host)
                .IsEqualTo("[fe80::1%zone]");
            await Assert.That(new Endpoint("[[::1]]", 1, "p").Host).IsEqualTo("[[::1]]");
        }
        var lowPort = Throws<ArgumentOutOfRangeException>(() =>
            _ = new Endpoint("127.0.0.1", 0, "p")
        );
        await Assert.That(lowPort).IsNotNull();
        var highPort = Throws<ArgumentOutOfRangeException>(() =>
            _ = new Endpoint("127.0.0.1", 65536, "p")
        );
        await Assert.That(highPort).IsNotNull();
        await Assert.That(new Endpoint("127.0.0.1", 1, "ção").Password).IsEqualTo("ção");
        var blankHost = Throws<ArgumentException>(() => _ = new Endpoint("  ", 1, "p"));
        await Assert.That(blankHost).IsNotNull();
        var blankPassword = Throws<ArgumentException>(() => _ = new Endpoint("127.0.0.1", 1, " "));
        await Assert.That(blankPassword).IsNotNull();
    }

    [Test]
    public async Task ClientInformation()
    {
        var ok = new ClientInformation(1);
        await Assert.That(ok.CurrentUserId).IsEqualTo(1);
    }

    [Test]
    [Arguments("[::1")]
    [Arguments("::1]")]
    [Arguments("[::1]x")]
    [Arguments("x[::1]")]
    public async Task HalfBracketedHostsReject(string host)
    {
        await Assert
            .That(() =>
            {
                _ = new Endpoint(host, 3000, "pass");
            })
            .Throws<ArgumentException>();
    }

    private sealed class ProbeException(string message, Exception? inner)
        : NyraException(message, inner);
}
