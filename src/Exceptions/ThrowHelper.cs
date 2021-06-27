using System;
using System.Diagnostics.CodeAnalysis;

namespace KeyValueSet.Exceptions
{
    public static class ThrowHelper
    {
        [DoesNotReturn]
        public static void ThrowInvalidOperationException_ConcurrentOperationsNotSupported()
        {
            throw new InvalidOperationException("Concurrent operations are not supported.");
        }

        public static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw new ArgumentOutOfRangeException(Enum.GetName(argument));
        }

        public static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw new ArgumentNullException(Enum.GetName(argument));
        }

        public static void ThrowArgumentException(string message)
        {
            throw new ArgumentException(message);
        }
    }

    public enum ExceptionArgument {
        array, capacity, other
    }
}
