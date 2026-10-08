namespace BookApp.Application.Exceptions;

public class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(
        string serviceName,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceName = serviceName;
    }

    public string ServiceName { get; }
}