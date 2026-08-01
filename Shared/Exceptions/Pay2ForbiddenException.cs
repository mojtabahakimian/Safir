using System;

namespace Safir.Shared.Exceptions
{
    public class Pay2ForbiddenException : Exception
    {
        public Pay2ForbiddenException(string message) : base(message)
        {
        }
    }
}
