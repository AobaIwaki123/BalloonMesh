using System.Net;

namespace BalloonMesh.DebugTool.Core;

public sealed class DebugApiException : Exception
{
    public DebugApiException(HttpStatusCode statusCode, string message, string? responseBody,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }
}
