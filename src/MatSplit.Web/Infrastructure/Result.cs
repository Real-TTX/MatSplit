namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Lightweight outcome type used by the service layer instead of exceptions
/// for expected validation failures. Razor Pages map <see cref="Error"/>
/// straight into <c>ModelState</c>.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>German, user facing error message. Null on success.</summary>
    public string? Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(string error) => new(false, error);

    public static Result<TValue> Ok<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Fail<TValue>(string error) => Result<TValue>.Failure(error);
}

/// <summary>
/// Outcome type carrying a payload on success.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? value;

    private Result(bool isSuccess, TValue? value, string? error)
        : base(isSuccess, error)
    {
        this.value = value;
    }

    /// <summary>The payload. Only valid when <see cref="Result.IsSuccess"/> is true.</summary>
    public TValue Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("Cannot access Value of a failed result.");

    /// <summary>Payload or default, safe to call on failures.</summary>
    public TValue? ValueOrDefault => value;

    public static Result<TValue> Success(TValue value) => new(true, value, null);

    public static Result<TValue> Failure(string error) => new(false, default, error);
}
