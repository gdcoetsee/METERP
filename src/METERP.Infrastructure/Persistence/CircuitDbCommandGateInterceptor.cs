using System.Data.Common;
using METERP.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace METERP.Infrastructure.Persistence;

/// <summary>
/// Ensures only one EF command runs at a time per scoped DbContext (Blazor Server circuit safety).
/// </summary>
public sealed class CircuitDbCommandGateInterceptor : DbCommandInterceptor
{
    private readonly CircuitDbContextGate _gate;

    public CircuitDbCommandGateInterceptor(CircuitDbContextGate gate) => _gate = gate;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        _gate.WaitAsync().GetAwaiter().GetResult();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        try
        {
            return base.ReaderExecuted(command, eventData, result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        _gate.WaitAsync().GetAwaiter().GetResult();
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        try
        {
            return base.NonQueryExecuted(command, eventData, result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        _gate.WaitAsync().GetAwaiter().GetResult();
        return base.ScalarExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        try
        {
            return base.ScalarExecuted(command, eventData, result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        _gate.Release();
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _gate.Release();
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData)
    {
        _gate.Release();
        base.CommandCanceled(command, eventData);
    }

    public override Task CommandCanceledAsync(
        DbCommand command,
        CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _gate.Release();
        return base.CommandCanceledAsync(command, eventData, cancellationToken);
    }
}