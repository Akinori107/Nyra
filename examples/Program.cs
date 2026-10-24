using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;
using Microsoft.Extensions.Logging;
using Nyra;
using Nyra.DSharpPlus;
using Nyra.Examples;
using Nyra.Exceptions;

var envFile = ParseEnvFile(".env");

var token = envFile.GetValueOrDefault("DISCORD_TOKEN");
if (string.IsNullOrEmpty(token))
    return Fail("token not configured");

var nodeHost = envFile.GetValueOrDefault("NODE_HOST");
if (string.IsNullOrEmpty(nodeHost))
    return Fail("node host not configured");

var nodePortValue = envFile.GetValueOrDefault("NODE_PORT");
if (!int.TryParse(nodePortValue, out var nodePort))
    return Fail("node port not configured");

var nodePassword = envFile.GetValueOrDefault("NODE_PASSWORD");
if (string.IsNullOrEmpty(nodePassword))
    return Fail("node password not configured");

var nodeSecure =
    bool.TryParse(envFile.GetValueOrDefault("NODE_SECURE"), out var parsedSecure) && parsedSecure;

var endpoint = new Endpoint(nodeHost, nodePort, nodePassword, nodeSecure);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
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
var commands = client.UseSlashCommands(new SlashCommandsConfiguration());
commands.RegisterCommands<MusicCommands>();
commands.SlashCommandErrored += OnSlashCommandErrored;

client.Ready += async (_, _) =>
{
    await nyra.ConnectAllNodesAsync().ConfigureAwait(false);

    foreach (var node in nyra.Nodes)
        if (node.IsConnected)
            client.Logger.LogInformation(
                "Audio node {Name}: {Software} {Version}.",
                node.Name,
                node.IsNodeLink ? "NodeLink" : "Lavalink",
                node.Version?.ToString() ?? "dev build"
            );
};

await client.ConnectAsync().ConfigureAwait(false);

try
{
    await Task.Delay(Timeout.Infinite, cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) { }
finally
{
    await client.DisconnectAsync().ConfigureAwait(false);
    await nyra.DisposeAsync().ConfigureAwait(false);
}

return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static Dictionary<string, string> ParseEnvFile(string path)
{
    var values = new Dictionary<string, string>();
    if (!File.Exists(path))
        return values;

    foreach (var line in File.ReadAllLines(path))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            continue;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex < 0)
            continue;

        values[line[..separatorIndex]] = line[(separatorIndex + 1)..];
    }

    return values;
}
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
            or IncompatibleServerException
            or VoiceHandshakeException
            or NotSupportedException
            or ArgumentException
        ? error.Exception.Message
        : "Something went wrong while running that command, check the logs for details.";

    var embed = new DiscordEmbedBuilder
    {
        Title = "Command failed",
        Description = message,
        Color = new DiscordColor(0xFF0000),
    };

    try
    {
        await error.Context.CreateResponseAsync(embed, ephemeral: true).ConfigureAwait(false);
    }
    catch
    {
        await error
            .Context.FollowUpAsync(
                new DiscordFollowupMessageBuilder().AddEmbed(embed).AsEphemeral()
            )
            .ConfigureAwait(false);
    }
}
