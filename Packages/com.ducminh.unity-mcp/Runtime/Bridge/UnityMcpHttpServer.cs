#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    public sealed class UnityMcpHttpServer : IDisposable
    {
        private const int MaxHeaderBytes = 32 * 1024;
        private const int MaxBodyBytes = 4 * 1024 * 1024;
        private const int MaxResponseBytes = 16 * 1024 * 1024;
        private readonly UnityMcpRegistry registry;
        private readonly UnityMcpScope scope;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private TcpListener listener;
        private Task acceptLoop;
        private string descriptorPath;

        public UnityMcpHttpServer(UnityMcpRegistry registry, UnityMcpScope scope)
        {
            this.registry = registry;
            this.scope = scope;
        }

        public UnityMcpInstanceDescriptor Descriptor { get; private set; }

        public void Start(UnityMcpInstanceDescriptor preferred = null)
        {
            if (listener != null) return;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, preferred?.port ?? 0);
                listener.Start(32);
                if (preferred == null)
                {
                    var tokenBytes = new byte[32];
                    using (var random = RandomNumberGenerator.Create()) random.GetBytes(tokenBytes);
                    var token = Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    Descriptor = UnityMcpDescriptorStore.Create(((IPEndPoint)listener.LocalEndpoint).Port, token, scope);
                }
                else
                {
                    Descriptor = preferred;
                    Descriptor.port = ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                descriptorPath = UnityMcpDescriptorStore.Write(Descriptor);
                acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token));
                Debug.Log($"UnityMCP listening on loopback port {Descriptor.port} ({Descriptor.kind}, {Descriptor.instanceId}).");
            }
            catch
            {
                try { listener?.Stop(); } catch { }
                listener = null;
                throw;
            }
        }

        public void Dispose()
        {
            shutdown.Cancel();
            try { listener?.Stop(); } catch { }
            listener = null;
            UnityMcpDescriptorStore.Delete(descriptorPath);
            shutdown.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) { Debug.LogWarning("UnityMCP accept failed: " + exception.Message); continue; }
                _ = HandleClientAsync(client, cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
        {
            using (client)
            {
                client.ReceiveTimeout = 35000;
                client.SendTimeout = 35000;
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote == null || !IPAddress.IsLoopback(remote.Address)) return;
                try
                {
                    using (var stream = client.GetStream())
                    {
                        var request = await ReadRequestAsync(stream, serverToken);
                        if (request == null) return;
                        if (!ValidHost(request.Headers.TryGetValue("host", out var host) ? host : null))
                        { await WriteJsonAsync(stream, 403, UnityMcpResult.Error("Invalid Host header."), null, serverToken); return; }
                        if (!request.Headers.TryGetValue("authorization", out var authorization) || !ConstantTimeToken(authorization))
                        { await WriteJsonAsync(stream, 401, UnityMcpResult.Error("Bearer authentication required."), null, serverToken, "Bearer"); return; }
                        await RouteAsync(stream, request, serverToken);
                    }
                }
                catch (InvalidDataException exception)
                {
                    try { await WriteJsonAsync(client.GetStream(), 400, UnityMcpResult.Error(exception.Message), null, serverToken); } catch { }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("UnityMCP request failed: " + exception.Message);
                }
            }
        }

        private async Task RouteAsync(NetworkStream stream, HttpRequest request, CancellationToken serverToken)
        {
            var path = request.Path.Split('?')[0];
            if (request.Method == "GET" && path == "/api/v1/health")
            {
                await WriteJsonAsync(stream, 200, new
                {
                    status = "ok", registryRevision = registry.RegistryRevision,
                    instance = PublicDescriptor(Descriptor)
                }, null, serverToken);
                return;
            }
            if (request.Method == "GET" && path == "/api/v1/tools")
            {
                var etag = "\"" + registry.RegistryRevision + "\"";
                if (request.Headers.TryGetValue("if-none-match", out var value) && value == etag)
                { await WriteEmptyAsync(stream, 304, etag, serverToken); return; }
                await WriteJsonAsync(stream, 200, new { registryRevision = registry.RegistryRevision, tools = registry.Tools }, etag, serverToken);
                return;
            }
            const string callPrefix = "/api/v1/tools/";
            const string callSuffix = "/call";
            if (request.Method == "POST" && path.StartsWith(callPrefix, StringComparison.Ordinal) && path.EndsWith(callSuffix, StringComparison.Ordinal))
            {
                if (!request.Headers.TryGetValue("content-type", out var contentType) || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                { await WriteJsonAsync(stream, 400, UnityMcpResult.Error("Content-Type must be application/json."), null, serverToken); return; }
                var encoded = path.Substring(callPrefix.Length, path.Length - callPrefix.Length - callSuffix.Length);
                var name = Uri.UnescapeDataString(encoded);
                if (name.Contains("/") || name.Contains("\\")) { await WriteJsonAsync(stream, 404, UnityMcpResult.Error("Unknown route."), null, serverToken); return; }
                JObject body;
                try
                {
                    body = string.IsNullOrWhiteSpace(request.Body) ? new JObject() : JObject.Parse(request.Body, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                }
                catch (JsonException) { await WriteJsonAsync(stream, 400, UnityMcpResult.Error("Request body must be valid JSON."), null, serverToken); return; }
                if (!(body["arguments"] is JObject arguments))
                { await WriteJsonAsync(stream, 400, UnityMcpResult.Error("arguments must be a JSON object."), null, serverToken); return; }
                var revision = body["registryRevision"]?.ToString();
                if (string.IsNullOrWhiteSpace(revision))
                { await WriteJsonAsync(stream, 400, UnityMcpResult.Error("registryRevision is required."), null, serverToken); return; }
                var started = DateTime.UtcNow;
                var result = await registry.InvokeAsync(name, arguments, revision, serverToken);
                Debug.Log($"UnityMCP audit tool={name} result={(result.isError ? "error" : "ok")} durationMs={(DateTime.UtcNow - started).TotalMilliseconds:F0}");
                await WriteJsonAsync(stream, ResultStatus(result), result, null, serverToken);
                return;
            }
            const string jobPrefix = "/api/v1/jobs/";
            if (path.StartsWith(jobPrefix, StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path.Substring(jobPrefix.Length));
                if (id.Contains("/") || id.Contains("\\")) { await WriteJsonAsync(stream, 404, UnityMcpResult.Error("Unknown route."), null, serverToken); return; }
                if (request.Method == "GET")
                {
                    if (!UnityMcpJobStore.Shared.TryGet(id, out var job)) { await WriteJsonAsync(stream, 404, UnityMcpResult.Error("Unknown job."), null, serverToken); return; }
                    await WriteJsonAsync(stream, 200, JobResponse(job), null, serverToken); return;
                }
                if (request.Method == "DELETE")
                {
                    if (!UnityMcpJobStore.Shared.Cancel(id, out var job)) { await WriteJsonAsync(stream, 404, UnityMcpResult.Error("Unknown job."), null, serverToken); return; }
                    await WriteJsonAsync(stream, 200, JobResponse(job), null, serverToken); return;
                }
            }
            await WriteJsonAsync(stream, 404, UnityMcpResult.Error("Unknown route."), null, serverToken);
        }

        private bool ValidHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim().ToLowerInvariant();
            return value == "127.0.0.1:" + Descriptor.port || value == "localhost:" + Descriptor.port;
        }

        private bool ConstantTimeToken(string authorization)
        {
            const string prefix = "Bearer ";
            if (authorization == null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            var provided = Encoding.UTF8.GetBytes(authorization.Substring(prefix.Length).Trim());
            var expected = Encoding.UTF8.GetBytes(Descriptor.token);
            if (provided.Length == 0 || expected.Length == 0) return false;
            var difference = provided.Length ^ expected.Length;
            for (var index = 0; index < Math.Max(provided.Length, expected.Length); index++)
                difference |= provided[index % provided.Length] ^ expected[index % expected.Length];
            return difference == 0;
        }

        private static object PublicDescriptor(UnityMcpInstanceDescriptor descriptor) => new
        {
            descriptor.port, descriptor.pid, descriptor.projectId, descriptor.instanceId, descriptor.kind, descriptor.buildId
        };

        private static object JobResponse(UnityMcpJob job) => new
        {
            job.jobId, job.status, job.result, job.error
        };

        private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var headerBytes = new List<byte>();
            var state = 0;
            while (state < 4)
            {
                var buffer = new byte[1];
                var count = await stream.ReadAsync(buffer, 0, 1, cancellationToken);
                if (count == 0) return null;
                headerBytes.Add(buffer[0]);
                if (headerBytes.Count > MaxHeaderBytes) throw new InvalidDataException("HTTP headers are too large.");
                state = (state == 0 && buffer[0] == 13) || (state == 2 && buffer[0] == 13) ? state + 1
                    : (state == 1 && buffer[0] == 10) || (state == 3 && buffer[0] == 10) ? state + 1 : 0;
            }
            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) throw new InvalidDataException("Malformed HTTP request line.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0) throw new InvalidDataException("Malformed HTTP header.");
                var key = line.Substring(0, separator).Trim().ToLowerInvariant();
                if (headers.ContainsKey(key)) throw new InvalidDataException("Duplicate HTTP headers are not accepted.");
                headers[key] = line.Substring(separator + 1).Trim();
            }
            if (headers.ContainsKey("transfer-encoding")) throw new InvalidDataException("Transfer-Encoding is not supported.");
            var length = 0;
            if (headers.TryGetValue("content-length", out var contentLength) && (!int.TryParse(contentLength, out length) || length < 0 || length > MaxBodyBytes))
                throw new InvalidDataException("Invalid or oversized Content-Length.");
            var bodyBytes = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var count = await stream.ReadAsync(bodyBytes, offset, length - offset, cancellationToken);
                if (count == 0) throw new InvalidDataException("Unexpected end of request body.");
                offset += count;
            }
            return new HttpRequest { Method = first[0].ToUpperInvariant(), Path = first[1], Headers = headers, Body = Encoding.UTF8.GetString(bodyBytes) };
        }

        private static async Task WriteJsonAsync(NetworkStream stream, int status, object value, string etag, CancellationToken cancellationToken, string authenticate = null)
        {
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
            settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            settings.Converters.Add(UnityMcpValueJsonConverter.Instance);
            var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, settings));
            if (payload.Length > MaxResponseBytes) { status = 500; payload = Encoding.UTF8.GetBytes("{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"Response exceeded size limit.\"}]}"); }
            var builder = new StringBuilder().Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n")
                .Append("Content-Type: application/json; charset=utf-8\r\n")
                .Append("Content-Length: ").Append(payload.Length).Append("\r\n")
                .Append("Cache-Control: no-store\r\nConnection: close\r\n");
            if (etag != null) builder.Append("ETag: ").Append(etag).Append("\r\n");
            if (authenticate != null) builder.Append("WWW-Authenticate: ").Append(authenticate).Append("\r\n");
            builder.Append("\r\n");
            var head = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(head, 0, head.Length, cancellationToken);
            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
        }

        private static async Task WriteEmptyAsync(NetworkStream stream, int status, string etag, CancellationToken cancellationToken)
        {
            var value = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {Reason(status)}\r\nContent-Length: 0\r\nETag: {etag}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(value, 0, value.Length, cancellationToken);
        }

        private static string Reason(int status)
        {
            switch (status) { case 200: return "OK"; case 304: return "Not Modified"; case 400: return "Bad Request"; case 401: return "Unauthorized"; case 403: return "Forbidden"; case 404: return "Not Found"; case 409: return "Conflict"; default: return "Internal Server Error"; }
        }

        private static int ResultStatus(UnityMcpResult result)
        {
            if (result == null || !result.isError || result.meta == null || !result.meta.TryGetValue("errorCode", out var code)) return 200;
            switch (Convert.ToString(code))
            {
                case "tool_not_found": return 404;
                case "tool_disabled": return 403;
                case "stale_registry": return 409;
                case "execution_failed": return 500;
                default: return 200;
            }
        }

        private sealed class HttpRequest
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers;
            public string Body;
        }
    }
}
#endif
