/// <summary>
/// Expected build failure (invalid option, missing tool, unexpected packaging output).
/// Reported as a single message, without stack trace.
/// </summary>
sealed class BuildFailedException(string message) : Exception(message);
