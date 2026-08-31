using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;
using Microsoft.Extensions.Logging;
using Nyra;
using Nyra.Examples;
using Nyra.Exceptions;

// The whole configuration of the example: a bot token and one audio node.
// Lavalink and NodeLink need no declaration, the node detects which one it is talking to.
string token = "your token here";
var endpoint = new Endpoint("localhost", 2333, "youshallnotpass"); // NodeLink default port: 3000

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Every Ctrl+C triggers a graceful shutdown.
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var client = new DiscordClient(
    new DiscordConfiguration
    {
        Token = token,
        TokenType = TokenType.Bot,
        Intents = DiscordIntents.AllUnprivileged,
    }
);

var nyra = client.UseNyra();
nyra.AddNode(new Node(endpoint, new NodeOptions { Name = "main", Resuming = true }));

// Registration must happen before ConnectAsync: command sync runs once, when the client
// becomes ready.
var commands = client.UseSlashCommands(new SlashCommandsConfiguration());
commands.RegisterCommands<MusicCommands>();
commands.SlashCommandErrored += OnSlashCommandErrored;

client.Ready += async (_, _) =>
{
    // A node that fails to connect keeps retrying in the background; nodes that are up
    // log their detected server kind here.
    await nyra.ConnectAllNodesAsync();

    foreach (var node in nyra.Nodes)
        if (node.IsConnected)
            client.Logger.LogInformation(
                "Audio node {Name}: {Software} {Version}.",
                node.Name,
                node.IsNodeLink ? "NodeLink" : "Lavalink",
                node.Version?.ToString() ?? "dev build"
            );
};

await client.ConnectAsync();

try
{
    await Task.Delay(Timeout.Infinite, cancellation.Token);
}
catch (OperationCanceledException) { }
finally
{
    await client.DisconnectAsync();
    await nyra.DisposeAsync();
}

return 0;

// Known library errors carry messages meant for the user (wrong channel, node not ready,
// unsupported feature); everything else stays generic while the details go to the logger.
static async Task OnSlashCommandErrored(
    SlashCommandsExtension extension,
    SlashCommandErrorEventArgs error
)
{
    extension.Client.Logger.LogError(
        error.Exception,
        "Slash command {Command} failed for {User}.",
        error.Context.CommandName,
        error.Context.User.Username
    );

    var message = error.Exception
        is InvalidOperationException
            or VoiceHandshakeException
            or NotSupportedException
            or ArgumentException
        ? error.Exception.Message
        : "Something went wrong while running that command; check the logs for details.";

    var embed = new DiscordEmbedBuilder
    {
        Title = "Command failed",
        Description = message,
        Color = new DiscordColor(0xFF0000),
    };

    try
    {
        await error.Context.CreateResponseAsync(embed, ephemeral: true);
    }
    catch
    {
        // The command already responded or deferred; fall back to a followup.
        await error.Context.FollowUpAsync(
            new DiscordFollowupMessageBuilder().AddEmbed(embed).AsEphemeral()
        );
    }
}
