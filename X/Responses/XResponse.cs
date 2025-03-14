

public class XResponse<T>
{
    public int StatusCode { get; private init; }
    public T? Content { get; private init; }
    public bool HasContent() => Content is not null;
    public string? ErrorTitle { get; private init; }
    public string? ErrorMessage { get; private init; }
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    public XResponse(int statusCode, T? content, string? errorTitle = null, string? errorMessage = null)
    {
        StatusCode = statusCode;
        Content = content;
        ErrorTitle = errorTitle;
        ErrorMessage = errorMessage;
    }
}