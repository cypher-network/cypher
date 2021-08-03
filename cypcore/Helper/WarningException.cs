using System;

namespace CYPCore.Helper
{
    public class WarningException : Exception
    {
        public override string Message { get; }

        public WarningException(string message)
        {
            Message = message;
        }
    }
}