using System;
using System.Runtime.Serialization;

namespace CYPCore.Helper
{
    public class QueueException : Exception
    {
        public QueueException()
        {
        }

        protected QueueException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        public QueueException(string message)
            : base(message)
        {
        }

        public QueueException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
