namespace Casino.Game.Errors.Exceptions;

public sealed class DomainException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
