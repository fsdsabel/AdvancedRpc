# AdvancedRPC

AdvancedRPC is a remote procedure call library for .NET. It differs from common solutions like REST, GRPC or WebSockets in that it supports an object hierarchy similar to .NET Remoting. I wrote the library mainly as a replacement for .NET Remoting to make our corporate application ready for .NET Core. It relies heavily on the ability to make remote procedure calls on objects.

## Features

- Communication via TCP and Named Pipes
- Support for impersonation with Named Pipes
- Deep object hierarchies
- Events and callbacks
- No need for serialization annotations, just publish an interface
- Support for multiple clients with notification on connection and disconnection
- .NET 4.8, .NET Standard 2.0 and .NET Standard 2.1
- **Very easy to setup:** No need to start a web service or define proto files. Just define an interface that is shared between applications and you are ready.

## Example

**Common interface definition**
```csharp

public interface IRpcServer 
{
    IRpcObject CreateObject(string name);
}

public interface IRpcObject 
{
    string Name { get; }

    void ChangeName(string name);

    event NameChanged;
}
```

**Server implementation**
```csharp

class RpcServer : IRpcServer 
{
    IRpcObject CreateObject(string name)
    {
        return RpcObjectImpl(name);
    }
}

class RpcObjectImpl : IRpcObject 
{
    public RpcObjectImpl(string name)
    {
        Name = name;
    }

    string Name { get; private set; }

    void ChangeName(string name)
    {
        Name = name;
        NameChanged?.Invoke(this, EventArgs.Empty);
    }

    event NameChanged;
}

class Program
{
    static async Task Main(string[] args)
    {
        var server = new NamedPipeRpcServerChannel(new BinaryRpcSerializer(), 
                            new RpcMessageFactory(), "myipcchannelname");
        server.ObjectRepository.RegisterSingleton<RpcServer>();
        await server.ListenAsync();

        Console.WriteLine("Press key to quit");
        Console.ReadKey();
    }
}

```

**Client implementation**

```csharp

class Program
{
    static async Task Main(string[] args)
    {
        var client = new NamedPipeRpcClientChannel(new BinaryRpcSerializer(), 
                            new RpcMessageFactory(), "myipcchannelname");        
        await client.ConnectAsync(TimeSpan.FromSeconds(5));

        var rpcServerObj = await client.GetServerObjectAsync<IRpcServer>();
        var nameObj = rpcServerObj.CreateObject("Jon Doe")
        nameObj.NameChanged += (sender, e) => Console.WriteLine(((IRpcObject)sender).Name);

        // This calls the method on the server and invokes
        // the event NameChanged on the client.
        nameObj.ChangeName("Jane Doe"); 

        Console.WriteLine("Press key to quit");
        Console.ReadKey();
    }
}

```

See unit tests for more advanced scenarios.

## Some Notes

- If you return a plain static object that doesn't need to know about server changes, use the `Serializable` attribute on the implementation. In that case the object will be serialized and copied to the client or server without creating a proxy object. This can be more efficient for data objects if you have a lot of properties and deep hierarchies. This behaves like a REST call.
- Do not return or pass IEnumerable, as this will result in a remote call for every `MoveNext` when iterating over it. Instead, use an array in those cases.
- Watch out for memory leaks. AdvancedRPC handles a lot of scenarios for you but take care to remove your event listeners.
- **CAREFUL!**  Every remote call can throw an exception if the server goes down.
- If you transfer large objects and use .NET Framework 4.7.2 or 4.8 please add an AppCompat switch in your App.config to improve performance

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <runtime>
    <!-- Use this switch to make BinaryFormatter fast with large object graphs starting with .NET 4.7.2 -->
      <AppContextSwitchOverrides value="Switch.System.Runtime.Serialization.UseNewMaxArraySize=true" />
  </runtime>
</configuration>
```

## Restrictions

- Method overloads with same parameter count are not supported (yet). Overloads with different parameter count are possible though.
- Named Pipe impersonation limitations:
    - doesn't work with .NET Standard 2.0 (for now)
    - only works on Windows
- `IEnumerable` doesn't work for .NET Core (the interfaces use ByRef Values). *There might be a workaround to support this.*
