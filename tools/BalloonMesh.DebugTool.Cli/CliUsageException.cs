namespace BalloonMesh.DebugTool.Cli;

/// <summary>Invalid command line usage. The process exits with code 2.</summary>
public sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}
