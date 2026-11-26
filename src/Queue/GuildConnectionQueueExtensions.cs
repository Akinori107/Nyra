namespace Nyra.Queue;

/// <summary>Gives a <see cref="GuildConnection"/> a <see cref="GuildQueue"/>.</summary>
public static class GuildConnectionQueueExtensions
{
    /// <summary>Gets the queue of the connection, and creates it on the first call.</summary>
    /// <param name="connection">The connection to get the queue of.</param>
    /// <returns>The queue of the connection, the same instance on every call.</returns>
    public static GuildQueue UseQueue(this GuildConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return GuildQueue.For(connection);
    }

    /// <summary>Gets the queue of the connection, if it has one.</summary>
    /// <param name="connection">The connection to get the queue of.</param>
    /// <returns>The queue of the connection, null when <see cref="UseQueue"/> was not called.</returns>
    public static GuildQueue? GetQueue(this GuildConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return GuildQueue.Find(connection);
    }
}
