using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VoiceType.Core.Logging;

namespace VoiceType.Core.Transcription;

/// <summary>
/// One dictation over the OpenAI Realtime transcription WebSocket:
/// 24 kHz mono PCM16 in, manual commit, partial deltas out, one final
/// transcript. Audio queues locally in a bounded channel while the socket
/// connects. Logs event names only — never transcript content or audio.
/// </summary>
internal sealed class OpenAiRealtimeSession : ITranscriptionSession
{
    // Protocol constants grouped here so a doc change is a one-place edit.
    private const string Endpoint = "wss://api.openai.com/v1/realtime?intent=transcription";
    private const string Model = "gpt-realtime-whisper";
    private const string EvtDelta = "conversation.item.input_audio_transcription.delta";
    private const string EvtCompleted = "conversation.item.input_audio_transcription.completed";
    private const string EvtError = "error";

    private const int ConnectTimeoutMs = 5_000;
    private const int MaxQueuedChunks = 2_048; // ~3.4 min of audio; overflow fails the session

    // Ceiling on one reassembled server message. Transcription events are
    // kilobytes; without a cap a stuck or hostile continuation stream grows
    // the buffer until the process dies mid-dictation.
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly string _apiKey;
    private readonly ILog _log;
    private readonly ClientWebSocket _ws = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(MaxQueuedChunks) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource<string> _finalTranscript =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _partial = new();
    private readonly Task _sendLoop;
    private readonly Task _receiveLoop;
    private volatile string _lastPartial = string.Empty;
    private volatile bool _cancelled;

    public event Action<string>? PartialTranscript;

    public string LastPartial => _lastPartial;

    public OpenAiRealtimeSession(string apiKey, ILog log)
    {
        _apiKey = apiKey;
        _log = log;
        // GA Realtime API: bearer auth only. The old "OpenAI-Beta: realtime=v1"
        // header selects the retired beta shape and the server refuses it
        // (beta_api_shape_disabled).
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

        var connected = ConnectAndConfigureAsync();
        _sendLoop = SendLoopAsync(connected);
        _receiveLoop = ReceiveLoopAsync(connected);
    }

    public void AddAudio(byte[] chunk)
    {
        if (_cancelled) return;
        if (!_outgoing.Writer.TryWrite(chunk))
        {
            _log.Error("Audio queue overflow; failing session.");
            _finalTranscript.TrySetException(new InvalidOperationException("Audio queue overflow."));
        }
    }

    public async Task<string> FinishAsync(CancellationToken cancellationToken)
    {
        _outgoing.Writer.TryComplete();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        try
        {
            await _sendLoop.WaitAsync(linked.Token).ConfigureAwait(false);
            await SendJsonAsync(new { type = "input_audio_buffer.commit" }, linked.Token).ConfigureAwait(false);
            return await _finalTranscript.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Cancel();
        }
    }

    public Task CancelAsync()
    {
        _cancelled = true;
        _outgoing.Writer.TryComplete();
        _finalTranscript.TrySetCanceled();
        _lifetime.Cancel();
        _ws.Abort();
        _log.Info("Transcription session cancelled.");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _outgoing.Writer.TryComplete();
        _ws.Abort();
        try { await Task.WhenAll(_sendLoop, _receiveLoop).ConfigureAwait(false); }
        catch { /* loop faults already surfaced via _finalTranscript */ }
        _ws.Dispose();
        _lifetime.Dispose();
        _partial.Clear();
    }

    private async Task ConnectAndConfigureAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(ConnectTimeoutMs);

        await _ws.ConnectAsync(new Uri(Endpoint), timeout.Token).ConfigureAwait(false);
        _log.Info("Realtime socket connected.");

        // GA transcription-session shape (verified live 2026-07-10): server
        // acks with session.updated echoing the model and null turn_detection.
        var config = new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = 24_000 },
                        transcription = new { model = Model },
                        turn_detection = (object?)null, // manual commit
                    },
                },
            },
        };
        await SendJsonAsync(config, timeout.Token).ConfigureAwait(false);
    }

    private async Task SendLoopAsync(Task connected)
    {
        try
        {
            await connected.ConfigureAwait(false);
            await foreach (var chunk in _outgoing.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                var append = new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(chunk) };
                await SendJsonAsync(append, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // cancel/shutdown
        }
        catch (Exception ex)
        {
            _log.Error("Realtime send loop failed.", ex);
            _finalTranscript.TrySetException(ex);
            throw;
        }
    }

    private async Task ReceiveLoopAsync(Task connected)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();

        try
        {
            await connected.ConfigureAwait(false);
            while (_ws.State == WebSocketState.Open && !_lifetime.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _finalTranscript.TrySetException(new InvalidOperationException("Socket closed before final transcript."));
                        return;
                    }

                    if (message.Length + result.Count > MaxMessageBytes)
                        throw new InvalidOperationException("Realtime message exceeded the size cap.");

                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleEvent(message);
            }
        }
        catch (OperationCanceledException)
        {
            // cancel/shutdown
        }
        catch (Exception ex)
        {
            _log.Error("Realtime receive loop failed.", ex);
            _finalTranscript.TrySetException(ex);
        }
    }

    private void HandleEvent(MemoryStream message)
    {
        using var doc = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
        string? type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (type)
        {
            case EvtDelta:
                if (doc.RootElement.TryGetProperty("delta", out var delta) && delta.GetString() is { } text)
                {
                    _partial.Append(text);
                    _lastPartial = _partial.ToString();
                    PartialTranscript?.Invoke(_lastPartial);
                }
                break;

            case EvtCompleted:
                string final = doc.RootElement.TryGetProperty("transcript", out var tr)
                    ? tr.GetString() ?? string.Empty
                    : _lastPartial;
                _finalTranscript.TrySetResult(final);
                break;

            case EvtError:
                string code = doc.RootElement.TryGetProperty("error", out var err)
                    && err.TryGetProperty("code", out var c) ? c.GetString() ?? "unknown" : "unknown";
                _log.Error($"Realtime API error event: {code}.");
                _finalTranscript.TrySetException(new InvalidOperationException($"Transcription error ({code})."));
                break;
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _ws.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
    }
}
