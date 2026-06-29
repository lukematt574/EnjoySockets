// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace EnjoySockets
{
    /// <summary>
    /// Defines a server-side authorization handler for client authentication.
    /// </summary>
    /// <typeparam name="T">
    /// Type of the credentials or authorization data supplied by the client.
    /// </typeparam>
    public interface IEAuthorization<T>
    {
        /// <summary>
        /// Validates the client authorization data and returns the authorization result.
        /// </summary>
        /// <param name="credentials">
        /// Client-provided credentials or authorization data.
        /// </param>
        /// <returns>
        /// A task that completes with an <see cref="EConnectResult"/> indicating whether the client has been successfully authorized.
        /// </returns>
        Task<EConnectResult> OnAuthorization(T credentials);
    }
}
