using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace SystemOneDecisionConsole.Tests;

public sealed record RequestRecord(string Path, JsonObject Payload, Dictionary<string, string> Headers);

public sealed record FixtureResponse(string Body, int StatusCode = 200)
{
    public double DelaySeconds { get; init; }
    public static FixtureResponse Json(JsonNode body, int status = 200) => new(body.ToJsonString(), status);
    public static FixtureResponse Raw(string body, int status = 200) => new(body, status);
}

public sealed class FixtureServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<RequestRecord> requests = [];
    private readonly object gate = new();
    private readonly ConcurrentBag<Task> handlers = [];
    private readonly ConcurrentBag<TcpClient> clients = [];
    private readonly ConcurrentQueue<Exception> callbackFailures = [];
    private readonly Task accepting;
    private int active;
    public string BaseUrl { get; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public Func<RequestRecord, int, FixtureResponse>? Responder { get; set; }
    public int MaximumActiveRequests { get; private set; }
    public List<RequestRecord> Requests { get { lock (gate) return requests.ToList(); } }

    public FixtureServer()
    {
        listener.Start();
        BaseUrl = $"http://127.0.0.1:{Port}";
        accepting = AcceptAsync();
    }

    public void Reset()
    {
        lock (gate)
        {
            requests.Clear();
            MaximumActiveRequests = active;
        }
        while (callbackFailures.TryDequeue(out _)) { }
    }

    public void ThrowIfCallbackFailed()
    {
        if (callbackFailures.TryDequeue(out Exception? error))
            throw new AssertFailedException(AssertEx.Diagnostic($"Mock HTTP callback failed: {error}"));
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellation.Token);
                clients.Add(client);
                handlers.Add(HandleAsync(client));
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException && cancellation.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        bool counted = false;
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var headerBytes = new List<byte>();
                byte[] one = new byte[1];
                while (headerBytes.Count < 65536)
                {
                    if (await stream.ReadAsync(one, cancellation.Token) == 0) return;
                    headerBytes.Add(one[0]);
                    int length = headerBytes.Count;
                    if (length >= 4 && headerBytes[length - 4] == 13 && headerBytes[length - 3] == 10 &&
                        headerBytes[length - 2] == 13 && headerBytes[length - 1] == 10) break;
                }
                string[] lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n", StringSplitOptions.None);
                string[] requestLine = lines[0].Split(' ');
                if (requestLine.Length != 3 || requestLine[0] != "POST") throw new InvalidDataException("Expected a POST request.");
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1).Where(line => line.Length > 0))
                {
                    int separator = line.IndexOf(':');
                    if (separator <= 0) throw new InvalidDataException("Malformed request header.");
                    headers[line[..separator].ToLowerInvariant()] = line[(separator + 1)..].Trim();
                }
                int contentLength = int.Parse(headers["content-length"], System.Globalization.CultureInfo.InvariantCulture);
                if (contentLength < 0 || contentLength > 32 * 1024 * 1024) throw new InvalidDataException("Invalid request body length.");
                byte[] body = new byte[contentLength];
                await stream.ReadExactlyAsync(body, cancellation.Token);
                var request = new RequestRecord(requestLine[1], JsonNode.Parse(body)!.AsObject(), headers);
                int ordinal;
                lock (gate)
                {
                    ordinal = requests.Count;
                    requests.Add(request);
                    active++;
                    counted = true;
                    MaximumActiveRequests = Math.Max(MaximumActiveRequests, active);
                }
                FixtureResponse response;
                try
                {
                    response = Responder?.Invoke(request, ordinal) ??
                        FixtureResponse.Json(TestSupport.AnswersFor(request.Payload, request.Path));
                }
                catch (Exception error)
                {
                    callbackFailures.Enqueue(error);
                    response = FixtureResponse.Raw("{\"error\":\"fixture response callback failed\"}", 500);
                }
                byte[] responseBytes = Encoding.UTF8.GetBytes(response.Body);
                string status = response.StatusCode switch { 200 => "OK", 401 => "Unauthorized", 403 => "Forbidden", 500 => "Internal Server Error", _ => "Fixture" };
                string responseHeaders = $"HTTP/1.1 {response.StatusCode} {status}\r\nContent-Type: application/json\r\nContent-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeaders), cancellation.Token);
                if (response.DelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(response.DelaySeconds), cancellation.Token);
                lock (gate) { active--; counted = false; }
                await stream.WriteAsync(responseBytes, cancellation.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException ||
                error is IOException or SocketException)
            {
                // A deliberately timed-out client may close while the fixture is returning its delayed response.
            }
            catch (Exception error) { callbackFailures.Enqueue(error); }
            finally { if (counted) lock (gate) active--; }
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listener.Stop();
        foreach (TcpClient client in clients) client.Dispose();
        accepting.GetAwaiter().GetResult();
        Task.WhenAll(handlers).GetAwaiter().GetResult();
        cancellation.Dispose();
    }
}
