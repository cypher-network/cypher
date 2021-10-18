using System;

namespace CYPCore.Helper
{
    /// <summary>
    /// 
    /// </summary>
    public class WarningException : Exception
    {
        public override string Message { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public WarningException(string message)
        {
            Message = message;
        }
    }
}