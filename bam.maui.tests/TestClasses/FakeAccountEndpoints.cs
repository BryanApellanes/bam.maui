using System.Net;
using System.Net.Http;
using System.Text;

namespace Bam.Maui.Tests.TestClasses;

/// <summary>One request the fake endpoints received.</summary>
public sealed record RecordedRequest(string Method, string Uri, string? AccountHeader, string? Cookie, bool HasOrigin, string? ContentType, string? Body);

/// <summary>One canned answer.</summary>
public sealed class FakeResponse
{
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public string? Body { get; init; }
    public string ContentType { get; init; } = "application/json";
    public List<string> SetCookies { get; init; } = new List<string>();
    public string? Location { get; init; }
    public bool OmitLength { get; init; }
    public Exception? Throw { get; init; }

    /// <summary>Send the headers, then never send the body.</summary>
    public bool StallBody { get; init; }

    /// <summary>Send the headers and part of the body, then drop the connection.</summary>
    public bool BreakBody { get; init; }
}

/// <summary>
/// The transport under <c>HttpPasskeyBackend</c>'s cookie jar in tests: answers from a queue and
/// records what was sent.  No network.
/// </summary>
public sealed class FakeAccountEndpoints : HttpMessageHandler
{
    private readonly Queue<FakeResponse> _responses = new Queue<FakeResponse>();

    public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

    public FakeAccountEndpoints Respond(HttpStatusCode status, string? body, params string[] setCookies)
    {
        _responses.Enqueue(new FakeResponse { Status = status, Body = body, SetCookies = setCookies.ToList() });
        return this;
    }

    public FakeAccountEndpoints Respond(FakeResponse response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public RecordedRequest Last => Requests[Requests.Count - 1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? cookie = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) ? string.Join("; ", cookies) : null;
        string? header = request.Headers.TryGetValues("X-Bam-Account", out IEnumerable<string>? values) ? string.Join(",", values) : null;
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, header, cookie, request.Headers.Contains("Origin"), request.Content?.Headers.ContentType?.ToString(), body));

        FakeResponse next = _responses.Count > 0 ? _responses.Dequeue() : new FakeResponse { Status = HttpStatusCode.NotFound };
        if (next.Throw is not null)
        {
            throw next.Throw;
        }
        HttpResponseMessage response = new HttpResponseMessage(next.Status);
        if (next.BreakBody)
        {
            StreamContent broken = new StreamContent(new BreakingStream(Encoding.UTF8.GetBytes("{\"passkeys\":[")));
            broken.Headers.TryAddWithoutValidation("Content-Type", next.ContentType);
            response.Content = broken;
        }
        else if (next.StallBody)
        {
            StreamContent stalled = new StreamContent(new StallingStream());
            stalled.Headers.TryAddWithoutValidation("Content-Type", next.ContentType);
            response.Content = stalled;
        }
        else if (next.Body is not null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(next.Body);
            HttpContent content = next.OmitLength ? new StreamContent(new UnknownLengthStream(bytes)) : new ByteArrayContent(bytes);
            content.Headers.TryAddWithoutValidation("Content-Type", next.ContentType);
            response.Content = content;
        }
        foreach (string setCookie in next.SetCookies)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }
        if (next.Location is not null)
        {
            response.Headers.Location = new Uri(next.Location);
        }
        return response;
    }

    /// <summary>A body that never arrives: every read waits until it is cancelled.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read asynchronously.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }
    }

    /// <summary>A body that sends its first bytes and then fails the way a dropped connection does.</summary>
    private sealed class BreakingStream : Stream
    {
        private readonly byte[] _start;
        private bool _sent;

        public BreakingStream(byte[] start)
        {
            _start = start;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read asynchronously.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                _start.CopyTo(buffer);
                return ValueTask.FromResult(_start.Length);
            }
            throw new HttpIOException(HttpRequestError.ResponseEnded, "connection to 10.0.0.7:443 reset by peer");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }
    }

    /// <summary>A stream that will not say how long it is, so the content has no Content-Length.</summary>
    private sealed class UnknownLengthStream : MemoryStream
    {
        public UnknownLengthStream(byte[] bytes) : base(bytes) { }

        public override bool CanSeek => false;
    }
}
