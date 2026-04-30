using EnjoySockets;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

string PublicPemKey = "-----BEGIN PUBLIC KEY-----MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA0KtdY+YWfC8cZl6MkYjgJlsoXv961lientvI9kHNwfUjwVgzQDMRUYkphGXUU8qLlnIaloZvZ/Mzd8F6WEXIvc6Nl34U/iecFI3NHh+OyM+C4d898w7IdN8HrO7yjsxELySt6XRgtKFbr2SAzJo9Ub3L2DVGpU+oxTNTeppI+sng6kESkeHMsHiunEczsCZkFAqhRzaN9QQQCfwJWLmXCJntU6Cv2IzYVBntgOr2+ieoZSpD1awcvi6zPJ/XVyYOmxhjT7thnpsabUtQbJ4woq2h+HQv+bIGvrQrjF5zVAE9eYka5aQcspZJgCpJ8SBv0EPLlw+z09azrCo64Pa9qyzg9hdx6IhkS/M3IacIpRI+NNibH77o4zvdrvLa/I71zKwWFlfCGGWz4LvpNbK4UQXxnDT8Iw5KvSLi0Emdi6n5C5qDntGSZFL7ALar0h105B2fgaSDRA5eAfoUvyWs0Wk2XNhcB1eoqOCAXfkVsSWlSleMNCyCFZKkmifbgjlOi29uxPs+Z9/Ed1V1gJsGO2e6ZjNL/TcoD9Vu6ivE/SbsnOt2TbpHepJTjoKd5VaOAAm/wu/yYvYAVfoV+GwkIOM6e9t7+zR4IUTjBWYEUiAZUPFOFgq1Gqi3b5pIaMyA0qGElLbtnWt09bBSys/CQ+X7uccPt11Ewg7r4voIiVUCAwEAAQ==-----END PUBLIC KEY-----";
string PublicPemKeyToSign = "-----BEGIN PUBLIC KEY-----MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA07hMWC7U9llKwfmdBYtk5wUtBYcqJIB7ufpJZsIryYWX2p2OV6oEQh7jjCcSYISgf7/u6BGNKjYGUm2vqkp/lJC04ZOIpw0zi7d/cRXNUknJ7A34pDutG1EmapWhDeoG9VmhZFT2w1dumM89yf49akjUSJCP+uC+nvhxIfMEPNJkQ9c595aWDHbjfbNwKvE9ZAmz+nen79pgzNc6NpoLKUFbOxZsLBrAgCtDQoLIW0PtC/rmucdj3RI7VFz9cd1CWFWV47pr/2XrqLUE7ncSIwfrG3906xOTxjZgGUDbHvU5FwU5p35LxoFs3rfby9UWuOA7vAq18bco9/+EnI1Cbgkhn0PwMCISmhPgQG0gbgGFTtdnKqST18h7cwgcxkjcoLms4TE9tNaFF8IkmRyG38f69chmiVzYO8fDw/EQ7PfWsV78wE0puonNj2iQRhhkECxzJvm4erQjqlbpBtOtlAZXL3GBgZHDG0eyJq3L7fU0tMfoAQrWtKGWHQI4wyWUEyQQYAJjD3IYTt/r8K3za/a2bpMyMKcL2eWtDvxceEV1Tuo2pNoD8kmYfs5aS1xJWwtxapVR43TnnRzoH7cv8lYNGzlzjgU+88E7VPknti0yqrQZ1VJXAV7/LfUI2VMmfVyp4wkQi5/ujiIsK7haS6QZCg48o5HDkC7vvz2LZaUCAwEAAQ==-----END PUBLIC KEY-----";

var client = new EUserClient(new ERSA(PublicPemKey, PublicPemKeyToSign));
if (await client.Connect(EAddress.Get()) == 0)
{
    long id = await client.SendWithResponse("RegisterUser");
    if (id > 0)
    {
        Console.WriteLine($"You have joined the chat room as User({id})");
        while (true)
        {
            var message = Console.ReadLine();

            var bytesMsg = Encoding.UTF8.GetBytes(message ?? "").ToList();
            if (bytesMsg.Count > 4000 || bytesMsg.Count < 1)
            {
                Console.WriteLine("Message length is out of range");
                continue;
            }

            if (!await client.Send("PushMsg", bytesMsg))
            {
                Console.WriteLine("Failed to send message");
                continue;
            }
        }
    }
    else
    {
        Console.WriteLine("Failed to join the room");
        Environment.Exit(0);
    }
}
else
{
    Console.WriteLine("Failed to connect to the server");
    Environment.Exit(0);
}

public static class MainChat
{
    [EAttr(PoolId = 1, MaxParamSize = 4096)]
    static void PushMsg(EUserClient user, List<byte> msg)
    {
        if (msg.Count <= 4)
            return;

        var spanMsg = CollectionsMarshal.AsSpan(msg);

        int userId = BinaryPrimitives.ReadInt32LittleEndian(spanMsg[..4]);
        string message = Encoding.UTF8.GetString(spanMsg[4..]);
        Console.WriteLine($"User({userId}): {message}");
    }
}