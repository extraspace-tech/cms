using System.Data.Common;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Platform.Data;

/// <summary>Warns about slow database commands with the SQL text and time; parameter values are never logged (S0 spec §4.4).</summary>
public sealed partial class SlowQueryInterceptor(IOptions<LoggingOptions> options, ILogger<SlowQueryInterceptor> logger) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Check(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Check(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Check(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Check(DbCommand command, CommandExecutedEventData eventData)
    {
        var elapsedMs = (long)eventData.Duration.TotalMilliseconds;
        if (elapsedMs > options.Value.SlowQueryMs) SlowCommand(logger, elapsedMs, command.CommandText);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slow database command took {elapsedMs} ms: {commandText}")]
    private static partial void SlowCommand(ILogger logger, long elapsedMs, string commandText);
}
