using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace KeyValueCollection.Exceptions
{
    public static class ThrowHelper
    {
        [DoesNotReturn]
        public static void ThrowInvalidOperationException_ConcurrentOperationsNotSupported()
        {
            throw new InvalidOperationException("Concurrent operations are not supported.");
        }

        [DoesNotReturn]
        public static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw new ArgumentOutOfRangeException(Enum.GetName(argument));
        }

        [DoesNotReturn]
        public static void ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument argument, object? value)
        {
            throw new ArgumentOutOfRangeException(Enum.GetName(argument), value, "Value must be greater or equal to zero.");
        }

        [DoesNotReturn]
        public static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw new ArgumentNullException(Enum.GetName(argument));
        }

        [DoesNotReturn]
        public static void ThrowArgumentException_ArrayCapacity(ExceptionArgument argument)
        {
            throw new ArgumentException("Array has insufficient capacity.", Enum.GetName(argument));
        }

        [DoesNotReturn]
        public static void ThrowKeyNotFoundException()
        {
            throw new KeyNotFoundException();
        }

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_EnumeratorVersionDiffers()
        {
            throw new InvalidOperationException("The enumerator version differs from the enumerable version.");
        }

        public static void ThrowNotSupportedException()
        {
            throw new NotSupportedException();
        }

        public static void ThrowIndexOutOfRangeException()
        {
            throw new IndexOutOfRangeException();
        }

        public static NotSupportedException GetNotSupportedException()
        {
            return new ();
        }
    }

    public enum ExceptionArgument {
        array, capacity, other, arrayIndex, count
    }
}
