// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Runtime.CompilerServices;

namespace EnjoySockets
{
    /// <summary>
    /// Transact result for 'SendTransact' method in <see cref="EClient"/>
    /// </summary>
    public readonly record struct ETransactResult(long Code)
    {
        /// <summary>
        /// Transact succeeded successfully.
        /// </summary>
        public static readonly ETransactResult Success = new(0);

        /// <summary>
        /// Transact execution failed due to an internal or runtime error.
        /// </summary>
        public static readonly ETransactResult ExecutionFailed = new(-1);

        /// <summary>
        /// Buffer is full and required memory could not be allocated.
        /// </summary>
        /// <remarks>
        /// Returned when <see cref="EConfig.MessageBuffer"/> reaches its maximum capacity.
        /// Normally this condition is prevented by the client-side buffering logic.
        /// </remarks>
        public static readonly ETransactResult BufferFull = new(-2);

        /// <summary>
        /// Session has expired and the response could not be retrieved.
        /// </summary>
        public static readonly ETransactResult SessionExpired = new(-3);

        /// <summary>
        /// Access denied due to missing permissions failure.
        /// </summary>
        /// <remarks>
        /// This result is returned when the target server endpoint is configured with <see cref="EAttr.Access"/> and access validation fails.
        /// </remarks>
        public static readonly ETransactResult AccessDenied = new(-4);

        /// <summary>
        /// Invalid payload or arguments provided (e.g. null, empty data, or deserialization failure).
        /// </summary>
        public static readonly ETransactResult InvalidPayload = new(-5);

        /// <summary>
        /// Indicates whether the transact operation completed successfully.
        /// </summary>
        public bool IsSuccess => Code == 0;

        /// <summary>
        /// Indicates whether the result represents a system-level error (negative codes).
        /// </summary>
        public bool IsSystemError => Code < 0;

        /// <summary>
        /// Indicates whether the result is a custom application-defined code within the valid custom range (1 .. 1_000_000).
        /// </summary>
        public bool IsCustomCode => Code > 0 && Code <= EServerSession.MinUniqueId;

        /// <summary>
        /// Indicates whether the result represents a server-generated entity identifier (Code &gt; 1_000_000).
        /// </summary>
        public bool IsEntityId => Code > EServerSession.MinUniqueId;

        public override string ToString()
            => Code switch
            {
                0 => nameof(Success),
                -1 => nameof(ExecutionFailed),
                -2 => nameof(BufferFull),
                -3 => nameof(SessionExpired),
                -4 => nameof(AccessDenied),
                -5 => nameof(InvalidPayload),
                > 0 when Code <= EServerSession.MinUniqueId => $"Custom({Code})",
                > EServerSession.MinUniqueId => $"EntityId({Code})",
                _ => $"Unknown({Code})"
            };

        public static implicit operator long(ETransactResult result) => result.Code;

        public static implicit operator ETransactResult(long code) => new(code);

        /// <summary>
        /// Converts an enum value to its <see cref="long"/> representation.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown when the enum value exceeds the valid range of <see cref="long"/>.
        /// </exception>
        public static implicit operator ETransactResult(Enum en) => new(Convert.ToInt64(en));

        /// <summary>
        /// Converts an enum value to its <see cref="long"/> representation.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown when the enum value exceeds the valid range of <see cref="long"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ToLong<T>(T value) where T : unmanaged, Enum
        {
            return Convert.ToInt64(value);
        }
    }
}
