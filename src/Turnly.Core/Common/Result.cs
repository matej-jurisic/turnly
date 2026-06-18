namespace Turnly.Core.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}

public sealed record Error(ErrorType Type, string Message)
{
    public static Error Validation(string message) => new(ErrorType.Validation, message);
    public static Error NotFound(string message) => new(ErrorType.NotFound, message);
    public static Error Conflict(string message) => new(ErrorType.Conflict, message);
    public static Error Unauthorized(string message) => new(ErrorType.Unauthorized, message);
    public static Error Forbidden(string message) => new(ErrorType.Forbidden, message);
}

public class Result
{
    public bool Succeeded { get; }
    public Error? Error { get; }

    protected Result(bool succeeded, Error? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Fail(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(Error error) => Result<T>.FailWith(error);
}

public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool succeeded, T? value, Error? error) : base(succeeded, error)
    {
        Value = value;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> FailWith(Error error) => new(false, default, error);
}
