namespace Medoz.X;

public class ErrorResponseException : System.Exception
{
    public ErrorResponse Error { get; internal set; } = new("", "", "", "");
    public ErrorResponseException() { }
    public ErrorResponseException(string message) : base(message) { }
    public ErrorResponseException(string message, ErrorResponse error) : base(message)
    {
        Error = error;
    }

    public ErrorResponseException(string message, System.Exception inner) : base(message, inner) { }
}
