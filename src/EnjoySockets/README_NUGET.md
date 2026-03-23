# EnjoySockets

**EnjoySockets** is a high-performance C# library designed for TCP communication, with UDP support currently on the roadmap. It bridges the gap between low-level socket programming and overly complex frameworks, offering a "batteries-included" experience.

## Why EnjoySockets?

While working on numerous client-server projects, I often found myself lacking a library that provided necessary functionality out of the box without massive boilerplate.

* **Performance-First:** Built with a zero-allocation focus and full `async` support.
* **Resource Control:** Robust flow control using `System.Threading.Channels` and object pooling.
* **Easy Adoption:** High-level abstractions like RPC and session reconnection that don't require major refactoring.

## Serialization & Models

The library leverages the **MemoryPack** engine for maximum efficiency.

> [!IMPORTANT]
> Your message models must be marked as `partial` and have the `[MemoryPackable]` attribute. For advanced scenarios, refer to the [MemoryPack documentation](https://github.com/Cysharp/MemoryPack).

## Installation

```powershell
Install-Package EnjoySockets

```

### Compatibility

EnjoySockets supports **.NET 6** and **.NET 8+**, including **Native AOT**.

* *Recommendation:* For maximum server-side performance, use **.NET 8 or newer**. This allows the JIT to perform hardware-specific optimizations that often outperform Native AOT in high-throughput scenarios.

### Native AOT Configuration

To use Native AOT, add these sections to your `.csproj` and ensure you include the assembly containing your logic/message classes to prevent the trimmer from removing them:

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

EnjoySockets uses an intuitive routing engine: incoming messages are automatically routed to methods in your logic classes that match the message name.

### Server Side

```csharp
using EnjoySockets;

// You can generate a random key via ERSA.GeneratePrivatePublicPEM() or any other method.
string pemKeyPrivate = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";
string pemKeyPrivateSign = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";

var server = new ETCPServer(new(pemKeyPrivate, pemKeyPrivateSign));
server.Start(EAddress.Get());

Console.ReadKey();

static class ExampleReceiveClassServer
{
    static void TestMethod(EUserServer user)
    {
        user.Send("ResponseTestMethod", Random.Shared.Next());
    }
}

```

### Client Side

```csharp
using EnjoySockets;

// Ensure these public keys match the private keys used by the server.
string pemKeyPublic = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";
string pemKeyPublicSign = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";

var client = new EUserClient(new(pemKeyPublic, pemKeyPublicSign));

if(await client.Connect(EAddress.Get()) == 0)
	await client.Send("TestMethod");

Console.ReadKey();

static class ExampleReceiveClassClient
{
    static void ResponseTestMethod(EUserClient user, int luckyNumber)
    {
        Console.WriteLine($"Your lucky number from server is: {luckyNumber}");
    }
}

```

## Encryption & Security

EnjoySockets implements a hybrid encryption stack to ensure data confidentiality, integrity, and authenticity. It combines **RSA** (identity/handshake), **ECDH** (key exchange), and **AES-256-GCM** (transport encryption).

---

**Full Documentation & Examples:** Visit the [GitHub Repository](https://github.com/lukematt574/EnjoySockets) for advanced usage, RPC details, and contribution guidelines.

---