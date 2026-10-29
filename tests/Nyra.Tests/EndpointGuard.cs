namespace Nyra.Tests;

public sealed class EndpointGuard
{
    private static async Task Rejects(string host, int port, string password)
    {
        await Assert
            .That(() =>
            {
                _ = new Endpoint(host, port, password);
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PasswordGuards()
    {
        await Assert.That(new Endpoint("127.0.0.1", 1, "sécret").Password).IsEqualTo("sécret");
        await Assert
            .That(new Endpoint("127.0.0.1", 1, "pas\u0007sword").Password)
            .IsEqualTo("pas\u0007sword");
    }

    [Test]
    public async Task BracketedGuards()
    {
        using (Assert.Multiple())
        {
            await Assert.That(new Endpoint("[[::1]]", 1, "pass").Host).IsEqualTo("[[::1]]");
            await Assert
                .That(new Endpoint("[fe80::1%eth0]", 1, "pass").Host)
                .IsEqualTo("[fe80::1%eth0]");
            await Assert.That(new Endpoint("[not-an-ip]", 1, "pass").Host).IsEqualTo("[not-an-ip]");
            await Assert.That(new Endpoint("[::]", 1, "pass").Host).IsEqualTo("[::]");
            await Assert
                .That(new Endpoint("[::ffff:192.168.010.5]", 1, "pass").Host)
                .IsEqualTo("[::ffff:192.168.010.5]");
        }

        var loopback = new Endpoint("[::1]", 3000, "pass");
        await Assert.That(loopback.Port).IsEqualTo(3000);
        var global = new Endpoint("[2001:db8::1]", 3000, "pass");
        await Assert.That(global.Port).IsEqualTo(3000);
    }

    [Test]
    public async Task BareHostGuards()
    {
        await Rejects("ws://example.com", 1, "pass").ConfigureAwait(false);
        await Rejects("example.com:3000", 1, "pass").ConfigureAwait(false);
        await Rejects("::1", 1, "pass").ConfigureAwait(false);
        await Rejects("host name", 1, "pass").ConfigureAwait(false);
        await Rejects("host/name", 1, "pass").ConfigureAwait(false);

        var named = new Endpoint("node.example.com", 3000, "pass");
        await Assert.That(named.Port).IsEqualTo(3000);
        var v4 = new Endpoint("127.0.0.1", 3000, "pass");
        await Assert.That(v4.Port).IsEqualTo(3000);
    }
}
