namespace Campfire.Application.Storage.Media;

/// <summary>What a finished subprocess left: its exit code and the tail of its standard error.</summary>
public sealed record ProcessOutcome( int ExitCode, string StandardErrorTail )
{
    public bool Succeeded => ExitCode == 0;
}
