using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Nyra;
using Nyra.DiscordNet;
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

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger("Nyra.Examples");
MusicCommands.Logger = logger;

using var client = new DiscordSocketClient(
    new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
    }
);

var nyra = client.UseNyra(logger);
nyra.AddNode(new Node(endpoint, new NodeOptions { Name = "main", RescuePlayback = true }));

using var interactions = new InteractionService(
    client,
    new InteractionServiceConfig { ThrowOnError = false }
);
await interactions.AddModuleAsync<MusicCommands>(null).ConfigureAwait(false);
interactions.SlashCommandExecuted += (command, context, result) =>
    OnSlashCommandExecutedAsync(logger, command, context, result);

client.InteractionCreated += interaction =>
    interactions.ExecuteCommandAsync(new SocketInteractionContext(client, interaction), null);
client.Ready += () =>
{
    _ = StartAsync(interactions, nyra, logger);
    return Task.CompletedTask;
};

await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
await client.StartAsync().ConfigureAwait(false);

try
{
    await Task.Delay(Timeout.Infinite, cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) { }
finally
{
    await client.StopAsync().ConfigureAwait(false);
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

static async Task StartAsync(InteractionService interactions, NyraExtension nyra, ILogger logger)
{
    try
    {
        await interactions.RegisterCommandsGloballyAsync().ConfigureAwait(false);
        await nyra.ConnectAllNodesAsync().ConfigureAwait(false);

        foreach (var node in nyra.Nodes)
            if (node.IsConnected)
                logger.LogInformation(
                    "Audio node {Name}: {Software} {Version}.",
                    node.Name,
                    node.IsNodeLink ? "NodeLink" : "Lavalink",
                    node.Version?.ToString() ?? "dev build"
                );
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Failed to register the commands or connect the audio nodes.");
    }
}

static async Task OnSlashCommandExecutedAsync(
    ILogger logger,
    SlashCommandInfo command,
    IInteractionContext context,
    IResult result
)
{
    if (result.IsSuccess)
        return;

    var exception = result is ExecuteResult { Exception: { } failure }
        ? (failure as InteractionException)?.InnerException ?? failure
        : null;
    var expected =
        exception
        is InvalidOperationException
            or IncompatibleServerException
            or VoiceHandshakeException
            or NotSupportedException
            or ArgumentException;

    string message;
    if (exception is null)
    {
        logger.LogWarning(
            "Slash command {Command} was rejected for {User}: {Reason}",
            command.Name,
            context.User.Username,
            result.ErrorReason
        );
        message = result.ErrorReason;
    }
    else if (expected)
    {
        logger.LogWarning(
            "Slash command {Command} failed for {User}: {Message}",
            command.Name,
            context.User.Username,
            exception.Message
        );
        message = exception.Message;
    }
    else
    {
        logger.LogError(
            exception,
            "Slash command {Command} failed for {User}.",
            command.Name,
            context.User.Username
        );
        message = "Something went wrong with that command. Check the logs for details.";
    }

    var embed = new EmbedBuilder
    {
        Title = "Command failed",
        Description = message,
        Color = new Color(0xFF0000),
    }.Build();

    if (!context.Interaction.HasResponded)
    {
        await context.Interaction.RespondAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        return;
    }

    await context.Interaction.DeleteOriginalResponseAsync().ConfigureAwait(false);
    await context.Interaction.FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
}
