using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class TimeoutDescriptionAndGuard
{
    private sealed class HostileBridge : IDiscordBridge, IDisposable
    {
        public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add { }
            remove { }
        }

        public Microsoft.Extensions.Logging.ILogger? Logger => null;

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(1UL));

        public void Dispose() => throw new InvalidOperationException("the hostile bridge dies");
    }

    [Test]
    public async Task DisposeWithHostileBridge()
    {
        var extension = new NyraExtension(new HostileBridge());
        await Assert
            .That(async () => await extension.DisposeAsync().ConfigureAwait(false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("the hostile bridge dies");
    }
}
