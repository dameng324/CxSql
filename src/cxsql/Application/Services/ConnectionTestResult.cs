namespace CxSql.Application.Services;

public sealed class ConnectionTestResult
{
    private ConnectionTestResult(bool succeeded, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public static ConnectionTestResult Success()
    {
        return new ConnectionTestResult(true, null);
    }

    public static ConnectionTestResult Failure(string errorMessage)
    {
        return new ConnectionTestResult(false, errorMessage);
    }
}
