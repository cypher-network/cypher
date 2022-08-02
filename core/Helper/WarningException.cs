using System;

namespace CypherNetwork.Helper;

/// <summary>
/// </summary>
public class WarningException : Exception
{
    /// <summary>
    /// </summary>
    /// <param name="message"></param>
    public WarningException(string message)
    {
        Message = message;
    }

    public override string Message { get; }
}