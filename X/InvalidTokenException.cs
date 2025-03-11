namespace Medoz.X;

public class InvalidTokenExceptionException : System.Exception
{
    public string AuthURL { get; internal set; } = string.Empty;
    public InvalidTokenExceptionException() { }
    public InvalidTokenExceptionException(string message) : base(message) { }
    public InvalidTokenExceptionException(string message, string authUrl) : base(message)
    {
        AuthURL = authUrl;
    }

    public InvalidTokenExceptionException(string message, System.Exception inner) : base(message, inner) { }
}