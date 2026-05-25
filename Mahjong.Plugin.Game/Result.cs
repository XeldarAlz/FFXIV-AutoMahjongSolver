namespace Mahjong.Plugin.Game;

public readonly struct Result<TValue, TError> where TError : struct
{
    private readonly TValue? value;
    private readonly TError error;

    public bool IsSuccess { get; }

    public TValue Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("Result is in failure state — check IsSuccess first.");

    public TError Error => IsSuccess
        ? throw new InvalidOperationException("Result is in success state — check IsSuccess first.")
        : error;

    private Result(TValue value)
    {
        this.value = value;
        error = default;
        IsSuccess = true;
    }

    private Result(TError error)
    {
        value = default;
        this.error = error;
        IsSuccess = false;
    }

    public static Result<TValue, TError> Success(TValue value) => new(value);
    public static Result<TValue, TError> Failure(TError error) => new(error);

    public T Match<T>(Func<TValue, T> onSuccess, Func<TError, T> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    public override string ToString() => IsSuccess
        ? $"Success({value})"
        : $"Failure({error})";
}
