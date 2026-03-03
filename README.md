# EnjoySockets
[![NuGet Version](https://img.shields.io/nuget/v/EnjoySockets)](https://www.nuget.org/packages/EnjoySockets/)

**EnjoySockets** is a high-performance C# library designed for TCP communication, with UDP support currently on the roadmap.

## Why EnjoySockets?

While working on numerous client-server projects, I often found myself lacking a "batteries-included" library that provided the necessary functionality out of the box. Many existing .NET libraries fall into two categories:
1. **Too low-level:** Simple at first glance, but requiring you to manually implement critical components like flow control, pooling, and endpoint management.
2. **Too complex:** Highly performant, but with a steep learning curve and a requirement to maintain significant amounts of boilerplate code throughout the project's lifecycle.

**EnjoySockets** bridges this gap by offering a comprehensive, performance-first suite of tools for socket programming.

## Key Features

* 🚀 **Performance:** Zero-allocation focus, full `async` support, and optimized binary serialization.
* 🛠️ **Ease of Use:** High-level abstractions including RPC, built-in session reconnection, and instance management.
* ⚖️ **Resource Control:** Robust flow control using `System.Threading.Channels` and object pooling.
* 🔌 **Non-invasive:** Designed for easy adoption into existing codebases without major refactoring.

## Serialization

The library leverages the **MemoryPack** engine by [neuecc](https://github.com/neuecc) to ensure the most efficient serialization possible. 
> [!IMPORTANT]
> For advanced serialization scenarios and proper attribute usage, please refer to the [MemoryPack documentation](https://github.com/Cysharp/MemoryPack).

## Installation

The library is available via NuGet:

```powershell
Install-Package EnjoySockets

```

### Compatibility & Performance

EnjoySockets supports **.NET 6** and **.NET 8+**, including **Native AOT** support.

> [!TIP]
> For maximum server-side performance, it is recommended to use **.NET 8 or newer** without Native AOT. This allows the JIT compiler to perform advanced, hardware-specific optimizations during runtime.

### Native AOT Configuration

To ensure your application works correctly with Native AOT, add the following sections to your `.csproj` file:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>

<ItemGroup>
  <TrimmerRootAssembly Include="EnjoySockets" />
  <TrimmerRootAssembly Include="YourProject.Logic" />
</ItemGroup>

```

## Quick Start

Below is a minimal example showing how to set up a server and a client. EnjoySockets uses an intuitive approach where incoming messages are automatically routed to methods that match the message name.

### Server Side

```csharp
using EnjoySockets;

// You can generate a random key via ERSA.GeneratePrivatePublicPEM() or any other method.
string pemKeyPrivate = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";
string pemKeyPrivateSign = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";

Console.WriteLine("Starting Server...");

var server = new ETCPServer(new(pemKeyPrivate, pemKeyPrivateSign));

if (server.Start(EAddress.Get()))
    Console.WriteLine("Server started successfully!");
else
    Console.WriteLine("Failed to start server.");

Console.ReadKey();

// Logic class: Methods are automatically invoked based on the message name sent by the client.
static class ExampleReceiveClassServer
{
    static void TestMethod(EUserServer user)
    {
        Console.WriteLine("Received 'TestMethod' from client. Sending response...");
        _ = user.Send("ResponseTestMethod", Random.Shared.Next());
    }
}

```

### Client Side

```csharp
using EnjoySockets;

Console.WriteLine("Starting Client...");

// Ensure these public keys match the private keys used by the server.
string pemKeyPublic = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";
string pemKeyPublicSign = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";

var client = new EUserClient(new(pemKeyPublic, pemKeyPublicSign));

// Connect returns 0 on success
byte connectResult = await client.Connect(EAddress.Get());

if (connectResult == 0)
{
    Console.WriteLine("Connected to server.");
    // Sending a message that triggers 'TestMethod' on the server
    await client.Send("TestMethod");
}
else
{
    Console.WriteLine($"Connection failed. Error code: {connectResult}");
    Console.ReadKey();
    return;
}

Console.ReadKey();

// Logic class: Handles responses from the server.
static class ExampleReceiveClassClient
{
    static void ResponseTestMethod(EUserClient user, int luckyNumber)
    {
        Console.WriteLine($"Your lucky number from server is: {luckyNumber}");
    }
}

```

## 🔐 Encryption & Security

EnjoySockets implements a hybrid encryption stack to ensure data confidentiality, integrity, and authenticity. It combines **RSA** (identity/handshake), **ECDH** (key exchange), and **AES-256-GCM** (transport encryption).

### 🛰️ Secure Handshake Process

The library uses a robust handshake to establish a secure channel while preventing Man-in-the-Middle (MITM) attacks.

#### 1. Initial Connection
* **Client Side:** The client generates an ephemeral **ECDH** key pair (default: `nistP384`). It prepares a `ConnectDTO` containing a unique `UserId`, a `TokenToReconnect`, and the public `Key`. This payload is encrypted using the server's **RSA Public Key**.
* **Server Side:** The server decrypts the packet, uses the `TokenToReconnect` as **Salt** to derive the unique AES session key, and responds with its own ECDH public key plus an **RSA Digital Signature**.
* **Finalization:** Both parties compute the shared secret. All subsequent traffic is encrypted with **AES-256-GCM**.

#### 2. Session Reconnection & Key Rotation
EnjoySockets features a "Fast Reconnect" mechanism that maintains high security through continuous **Key Rotation**.

| Feature | Description |
| :--- | :--- |
| **Window** | Available within the `ETCPServerConfig.KeepAlive` timeframe (default: 60s). |
| **Token Rotation** | During reconnect, the client sends both the current `TokenToReconnect` (for identification) and a `NewTokenToReconnect`. |
| **Dynamic Re-keying** | Even during a simple reconnect, the server **re-mixes the AES key** using the new salt. This ensures that session keys are rotated and never static. |
| **Integrity** | If the tokens do not match the server-side state, the reconnection is rejected. |

---

### 🛡️ Cryptographic Standards

* **RSA:** Identity verification and secure delivery of the initial ECDH exchange.
* **ECDH (P-384):** Provides **Forward Secrecy**. Configurable via `ETCPConfig.ECCurve`.
* **AES-256-GCM:** Authenticated encryption (AEAD) for all messages, preventing both eavesdropping and data tampering.
* **Salted KDF:** Continuous key derivation using revolving tokens to ensure session freshness.

---

> [!TIP]
> Use `ERSA.GeneratePrivatePublicPEM()` to generate your initial key pairs for the server and client configuration.
