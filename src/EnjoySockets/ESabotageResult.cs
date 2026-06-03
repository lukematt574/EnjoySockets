// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Runtime.CompilerServices;

namespace EnjoySockets
{
    /// <summary>
    /// Represents an anomalous, suspicious, or unexpected activity detected from the client side.
    /// </summary>
    public readonly record struct ESabotageResult(int Code)
    {
        /// <summary>
        /// The arguments sent by the client to the target endpoint method are corrupted
        /// </summary>
        public static readonly ESabotageResult ArgCorrupted = new(1);

        /// <summary>
        /// The client is flooding the server (continuously sending data without reading responses)
        /// </summary>
        public static readonly ESabotageResult Flooding = new(2);

        public override string ToString()
            => Code switch
            {
                1 => nameof(ArgCorrupted),
                2 => nameof(Flooding),
                _ => $"Custom({Code})"
            };

        public static implicit operator int(ESabotageResult result) => result.Code;

        public static implicit operator ESabotageResult(int code) => new(code);

        /// <summary>
        /// Converts an enum value to its <see cref="int"/> representation.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown when the enum value exceeds the valid range of <see cref="int"/>.
        /// </exception>
        public static implicit operator ESabotageResult(Enum en) => new(Convert.ToInt32(en));

        /// <summary>
        /// Converts an enum value to its <see cref="int"/> representation.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown when the enum value exceeds the valid range of <see cref="int"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToInt<T>(T value) where T : unmanaged, Enum
        {
            return Convert.ToInt32(value);
        }
    }
}
