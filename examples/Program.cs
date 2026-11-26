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

client.Log += message =>
{
    logger.Log(
        message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        },
        message.Exception,
        "Discord.Net ({Source}): {Message}",
        message.Source,
        message.Message
    );
    return Task.CompletedTask;
};

var nyra = client.UseNyra(logger);
nyra.AddNode(
    new Node(
        endpoint,
        new NodeOptions
        {
            Name = "main",
            RescuePlayback = true,
            IdleTimeout = TimeSpan.FromSeconds(90),
            AloneTimeout = TimeSpan.FromSeconds(30),
            PauseWhenAlone = true,
        }
    )
);

LogNodeEvents(nyra, logger);

using var interactions = new InteractionService(
    client,
    new InteractionServiceConfig { ThrowOnError = false }
);
await interactions.AddModuleAsync<MusicCommands>(null).ConfigureAwait(false);
interactions.SlashCommandExecuted += (command, context, result) =>
    OnSlashCommandExecutedAsync(logger, command, context, result);

client.InteractionCreated += interaction =>
    interactions.ExecuteCommandAsync(new SocketInteractionContext(client, interaction), null);
var started = false;
client.Ready += () =>
{
    if (!started)
    {
        started = true;
        _ = StartAsync(interactions, nyra, logger);
    }
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

static void LogNodeEvents(NyraExtension nyra, ILogger logger)
{
    var readyBefore = new HashSet<Node>();
    nyra.NodeReady += (sender, args) =>
    {
        var node = (Node)sender!;
        if (!readyBefore.Add(node))
        {
            if (args.Attempts > 1)
                logger.LogInformation(
                    "Audio node {Name} reconnected after {Attempts} attempts.",
                    node.Name,
                    args.Attempts
                );
            else
                logger.LogInformation("Audio node {Name} reconnected.", node.Name);
            return Task.CompletedTask;
        }

        var software = node.IsNodeLink ? "NodeLink" : "Lavalink";
        var version = node.Version?.ToString() ?? "dev build";
        if (args.Attempts > 1)
            logger.LogInformation(
                "Audio node {Name}: {Software} {Version}, connected after {Attempts} attempts.",
                node.Name,
                software,
                version,
                args.Attempts
            );
        else
            logger.LogInformation(
                "Audio node {Name}: {Software} {Version}.",
                node.Name,
                software,
                version
            );
        return Task.CompletedTask;
    };

    nyra.NodeDisconnected += (sender, args) =>
    {
        logger.LogWarning(
            args.Exception,
            "Audio node {Name} disconnected ({Reason}).",
            ((Node)sender!).Name,
            args.CloseStatus?.ToString() ?? "connection lost"
        );
        return Task.CompletedTask;
    };

    nyra.NodeError += (_, args) =>
    {
        logger.LogError(args.Exception, "Audio node {Name} reported an error.", args.Node.Name);
        return Task.CompletedTask;
    };
}

static async Task StartAsync(InteractionService interactions, NyraExtension nyra, ILogger logger)
{
    try
    {
        await interactions.RegisterCommandsGloballyAsync().ConfigureAwait(false);
        await nyra.ConnectAllNodesAsync().ConfigureAwait(false);
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
