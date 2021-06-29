using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace KeyValueCollection.Exceptions
{
    internal static class ThrowHelper
    {
        private static readonly Dictionary<ExceptionArgument, string> s_argumentNameMap = new();

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_ConcurrentOperationsNotSupported()
        {
            throw new InvalidOperationException("Concurrent operations are not supported.");
        }

        [DoesNotReturn]
        public static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw new ArgumentOutOfRangeException(GetArgumentName(argument));
        }

        [DoesNotReturn]
        public static void ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument argument, object? value)
        {
            throw new ArgumentOutOfRangeException(GetArgumentName(argument), value, "Value must be greater or equal to zero.");
        }

        [DoesNotReturn]
        public static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw new ArgumentNullException(GetArgumentName(argument));
        }

        [DoesNotReturn]
        public static void ThrowArgumentException_ArrayCapacity(ExceptionArgument argument)
        {
            throw new ArgumentException("Array has insufficient capacity.", GetArgumentName(argument));
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

        public static string GetArgumentName(ExceptionArgument argument)
        {
            if (s_argumentNameMap.TryGetValue(argument, out string? name))
                return name;
            name = Enum.GetName(typeof(ExceptionArgument), argument)!;
            s_argumentNameMap.Add(argument, name);
            return name;
        }
    }

    public enum ExceptionArgument {
        array, capacity, other, arrayIndex, count
    }
}
