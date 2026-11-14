using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class HomeAssistantEvents : IDisposable
{
    readonly CancellationTokenSource stop = new CancellationTokenSource();
    readonly Settings settings;
    readonly Action<string> changed;
    bool disposed;
    readonly Task run;

    internal HomeAssistantEvents(Settings settings, Action<string> changed)
    {
        this.settings = settings;
        this.changed = changed;
        var token = stop.Token;
        run = Run(token);
    }

    async Task Run(CancellationToken cancellation)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using (var socket = new ClientWebSocket())
                {
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    var uri = new UriBuilder(settings.Url.TrimEnd('/') + "/api/websocket");
                    uri.Scheme = uri.Scheme == "https" ? "wss" : "ws";
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        handshake.CancelAfter(TimeSpan.FromSeconds(10));
                        var token = handshake.Token;
                        await socket.ConnectAsync(uri.Uri, token).ConfigureAwait(false);
                        Require(await Read(socket, json, token).ConfigureAwait(false), "auth_required");
                        await Write(socket, json.Serialize(new { type = "auth", access_token = settings.ReadToken() }), token)
                            .ConfigureAwait(false);
                        Require(await Read(socket, json, token).ConfigureAwait(false), "auth_ok");
                        await Write(socket, json.Serialize(new { id = 1, type = "subscribe_events", event_type = "state_changed" }), token)
                            .ConfigureAwait(false);
                        var result = await Read(socket, json, token).ConfigureAwait(false);
                        if (result.type != "result" || result.id != 1 || !result.success)
                            throw new IOException("Home Assistant rejected the event subscription.");
                    }

                    changed(null); // Close any gap during startup or reconnection.
                    while (!cancellation.IsCancellationRequested)
                    {
                        var message = await Read(socket, json, cancellation).ConfigureAwait(false);
                        if (message.type == "event" && message.id == 1 && message.@event != null &&
                            message.@event.event_type == "state_changed" && message.@event.data != null)
                        {
                            var entity = message.@event.data.entity_id;
                            if (!string.IsNullOrEmpty(entity)) changed(entity);
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (cancellation.IsCancellationRequested) break;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    static void Require(Message message, string type)
    {
        if (message.type != type) throw new IOException("Unexpected Home Assistant WebSocket response.");
    }

    static Task Write(ClientWebSocket socket, string json, CancellationToken token)
    {
        return socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, token);
    }

    static async Task<Message> Read(ClientWebSocket socket, JavaScriptSerializer serializer, CancellationToken token)
    {
        var buffer = new byte[8192];
        using (var message = new MemoryStream())
        {
            WebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > serializer.MaxJsonLength)
                    throw new IOException("Invalid Home Assistant WebSocket message.");
                message.Write(buffer, 0, part.Count);
            } while (!part.EndOfMessage);
            var result = serializer.Deserialize<Message>(Encoding.UTF8.GetString(message.ToArray()));
            if (result == null) throw new IOException("Missing Home Assistant WebSocket message.");
            return result;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        run.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }

    public sealed class Message
    {
        public string type { get; set; }
        public int id { get; set; }
        public bool success { get; set; }
        public Event @event { get; set; }
    }

    public sealed class Event
    {
        public string event_type { get; set; }
        public EventData data { get; set; }
    }

    public sealed class EventData
    {
        public string entity_id { get; set; }
    }
}