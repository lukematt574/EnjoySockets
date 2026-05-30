// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Runtime.CompilerServices;

namespace EnjoySockets
{
    /// <summary>
    /// Connection result for 'Connect' or 'ConnectWithAutoReconnect' method in <see cref="EClient"/>
    /// </summary>
    public readonly record struct EConnectResult(byte Code)
    {
        /// <summary>
        /// Connection succeeded.
        /// </summary>
        public static readonly EConnectResult Success = new(0);

        /// <summary>
        /// The provided endpoint (IP/address) is invalid or malformed.
        /// </summary>
        public static readonly EConnectResult InvalidEndpoint = new(1);

        /// <summary>
        /// Connection attempt timed out before a response was received.
        /// </summary>
        /// <remarks>
        /// This result is triggered when the server does not respond within the
        /// configured <see cref="EClientConfig.ConnectTimeout"/> period.
        /// </remarks>
        public static readonly EConnectResult Timeout = new(2);

        /// <summary>
        /// Server has reached its maximum capacity and cannot accept new connections.
        /// </summary>
        public static readonly EConnectResult ServerFull = new(3);

        /// <summary>
        /// Server verification failed during handshake or initial validation.
        /// </summary>
        public static readonly EConnectResult ServerVerificationFailed = new(4);

        /// <summary>
        /// Failed to establish secure connection (e.g., encryption handshake error).
        /// </summary>
        public static readonly EConnectResult HandshakeFailed = new(5);

        /// <summary>
        /// Server is unavailable or connection could not be established.
        /// </summary>
        public static readonly EConnectResult ServerUnavailable = new(6);

        /// <summary>
        /// Authentication data provided by the client is invalid or rejected.
        /// </summary>
        public static readonly EConnectResult InvalidAuthData = new(7);

        /// <summary>
        /// Connection is already established or currently in progress.
        /// </summary>
        public static readonly EConnectResult AlreadyConnectedOrConnecting = new(8);

        /// <summary>
        /// Reconnection attempt was cancelled (e.g., due to explicit disconnect).
        /// </summary>
        public static readonly EConnectResult ReconnectCancelled = new(9);

        /// <summary>
        /// Indicates whether the connection operation succeeded.
        /// </summary>
        public bool IsSuccess => Code == 0;

        /// <summary>
        /// Indicates whether the connection operation failed.
        /// </summary>
        public bool IsFailure => Code != 0;

        /// <summary>
        /// Indicates whether the result is a predefined system error code.
        /// </summary>
        public bool IsSystemError => Code >= 1 && Code <= 9;

        /// <summary>
        /// Indicates whether the result is a custom user-defined error code (&gt; 9).
        /// </summary>
        public bool IsCustomError => Code > 9;

        public override string ToString()
            => Code switch
            {
                0 => nameof(Success),
                1 => nameof(InvalidEndpoint),
                2 => nameof(Timeout),
                3 => nameof(ServerFull),
                4 => nameof(ServerVerificationFailed),
                5 => nameof(HandshakeFailed),
                6 => nameof(ServerUnavailable),
                7 => nameof(InvalidAuthData),
                8 => nameof(AlreadyConnectedOrConnecting),
                9 => nameof(ReconnectCancelled),
                _ => $"Custom({Code})"
            };

        public static implicit operator byte(EConnectResult result) => result.Code;

        public static implicit operator EConnectResult(byte code) => new(code);

        /// <summary>
        /// Converts an enum value to its <see cref="byte"/> representation.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown when the enum value exceeds the valid range of <see cref="byte"/>.
        /// </exception>
        public static implicit operator EConnectResult(Enum en) => new(Convert.ToByte(en));

        /// <summary>
        /// Converts an enum value to its <see cref="byte"/> representation.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown when the enum value exceeds the valid range of <see cref="byte"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToByte<T>(T value) where T : unmanaged, Enum
        {
            return Convert.ToByte(value);
        }
    }
}
