namespace SqlAnywhereMcp;

/// <summary>The outcome of an operation that can fail: <see cref="Ok{T}"/> with a value or <see cref="Err{T}"/> with a message.</summary>
public abstract record Result<T>
{
    public bool IsOk => this is Ok<T>;

    public Result<TU> Map<TU>(Func<T, TU> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return this switch
        {
            Ok<T> ok => new Ok<TU>(f(ok.Value)),
            Err<T> err => err.As<TU>(),
            _ => throw new InvalidOperationException("Unhandled Result variant"),
        };
    }

    public Result<TU> AndThen<TU>(Func<T, Result<TU>> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return this switch
        {
            Ok<T> ok => f(ok.Value),
            Err<T> err => err.As<TU>(),
            _ => throw new InvalidOperationException("Unhandled Result variant"),
        };
    }
}

public sealed record Ok<T>(T Value) : Result<T>;

/// <summary>Why an operation failed.</summary>
public enum ErrorKind
{
    /// <summary>The operation itself failed.</summary>
    Failed,

    /// <summary>The request is invalid, e.g. a procedure that is not allowlisted.</summary>
    Usage,

    /// <summary>config.json is missing, malformed or incomplete.</summary>
    Config,

    /// <summary>The password command failed.</summary>
    Credential,
}

public sealed record Err<T>(string Message, ErrorKind Kind = ErrorKind.Failed) : Result<T>
{
    /// <summary>The same error as a result of another type.</summary>
    public Err<TU> As<TU>() => new(Message, Kind);
}
