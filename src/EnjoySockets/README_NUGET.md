# EnjoySockets

**EnjoySockets** is a lightweight, low-latency TCP runtime for C# focused on message-driven architecture, RPC routing, and predictable concurrency.

Instead of dealing with raw sockets, threading, and synchronization, you build your network layer using simple attributes and message handlers.

Built for fast client-server systems with strong thread safety, low allocations, and stable performance under load.

## Features

* **Deterministic Concurrency** - control execution order with attributes - globally or per connection - without manual locks.

* **Low Allocation Runtime** - built-in pooling minimizes GC pressure and reduces memory overhead.

* **Backpressure & Flow Control** - automatically slows senders when buffers become saturated.

* **Declarative Security** - protect endpoints with attributes, payload limits, and permission-based routing.

* **Communication Patterns**

  * Fire & Forget
  * Request / Response
  * Transactional messaging

* **Serializer Friendly** - works well with serializers like MemoryPack and others.

## Designed For

* Developers who want to focus on application logic instead of socket infrastructure.
* Applications requiring predictable message processing and safe concurrency.
* Multiplayer, realtime, backend, and service-oriented applications.

## Quick Start

EnjoySockets uses an intuitive routing engine: incoming messages are automatically routed to methods in your logic classes that match the message name.

### Server Side

```csharp
using EnjoySockets;

// You can generate a random key via ERSA.GeneratePrivatePublicPEM() or any other method.
string pemKeyPrivate = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";
string pemKeyPrivateSign = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";

var server = new EServer(new(pemKeyPrivate, pemKeyPrivateSign));
server.Start(EAddress.Get());

Console.ReadKey();

static class ExampleReceiveClassServer
{
    static void TestMethod(EServerSession session)
    {
        session.Send("ResponseTestMethod", Random.Shared.Next());
    }
}

```

### Client Side

```csharp
using EnjoySockets;

// Ensure these public keys match the private keys used by the server.
string pemKeyPublic = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";
string pemKeyPublicSign = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";

var client = new EClient(new(pemKeyPublic, pemKeyPublicSign));

if(await client.Connect(EAddress.Get()).IsSuccess)
	await client.Send("TestMethod");

Console.ReadKey();

static class ExampleReceiveClassClient
{
    static void ResponseTestMethod(EClient client, int luckyNumber)
    {
        Console.WriteLine($"Your lucky number from server is: {luckyNumber}");
    }
}

```

---

**Full Documentation & Examples:** Visit the [GitHub Repository](https://github.com/lukematt574/EnjoySockets) for advanced usage, RPC details, and contribution guidelines.

---