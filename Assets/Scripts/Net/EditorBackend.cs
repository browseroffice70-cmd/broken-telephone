#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BrokenTelephone
{
    /// <summary>
    /// Editor-only stand-in for the browser plugin: a real WebSocket to the game server, plus a
    /// simulated microphone (types a fixed phrase as the "transcript") and simulated playback.
    /// Lets you sit in a room in Play mode next to real browser players.
    /// </summary>
    public sealed class EditorBackend : MonoBehaviour
    {
        const string SimulatedSpeech = "this is the editor speaking a test sentence";

        readonly ConcurrentQueue<(string method, string arg)> inbox = new ConcurrentQueue<(string, string)>();
        ClientWebSocket ws;
        CancellationTokenSource cts;
        Coroutine recording;
        internal bool hasTurnMedia;

        public void Callback(string method, string arg) => inbox.Enqueue((method, arg));

        void Update()
        {
            while (inbox.TryDequeue(out var m)) gameObject.SendMessage(m.method, m.arg, SendMessageOptions.DontRequireReceiver);
        }

        public async void Connect(string url)
        {
            Close();
            ws = new ClientWebSocket();
            cts = new CancellationTokenSource();
            var socket = ws;
            try
            {
                await socket.ConnectAsync(new Uri(url), cts.Token);
                Callback("OnNetOpen", "");
                await ReceiveLoop(socket, cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BT] Editor socket: " + e.Message + " — is the server running? (cd Server && npm start)");
            }
            if (ws == socket) Callback("OnNetClosed", "");
        }

        async Task ReceiveLoop(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var res = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (res.MessageType == WebSocketMessageType.Close) break;
                if (res.MessageType == WebSocketMessageType.Binary)
                {
                    // Drain the binary frame; the Editor cannot play Opus blobs, so just signal readiness.
                    int kind = buffer[0], index = (buffer[1] << 8) | buffer[2]; // header is in the first chunk
                    while (!res.EndOfMessage) res = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (kind == 1) { hasTurnMedia = true; Callback("OnTurnAudioReady", ""); }
                    else Callback("OnResultPlaybackEnded", index.ToString());
                    continue;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                if (!res.EndOfMessage) continue;
                Callback("OnNetMessage", sb.ToString());
                sb.Clear();
            }
        }

        public void Send(string json)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            _ = ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }

        public void SimulateRecording(int ms)
        {
            if (recording != null) StopCoroutine(recording);
            recording = StartCoroutine(RecordRoutine(ms));
        }

        IEnumerator RecordRoutine(int ms)
        {
            yield return new WaitForSecondsRealtime(ms / 1000f);
            FinishRecording();
        }

        public void FinishRecording()
        {
            if (recording != null) { StopCoroutine(recording); recording = null; }
            Send("{\"t\":\"turnDone\",\"transcript\":\"" + SimulatedSpeech + "\",\"voiced\":true}");
            Callback("OnRecordingFinished", "{\"transcript\":\"" + SimulatedSpeech + "\",\"voiced\":true}");
        }

        public bool SimulatePlayback(string endMethod, string arg)
        {
            if (!hasTurnMedia) return false;
            hasTurnMedia = false;
            StartCoroutine(After(1.5f, endMethod, arg));
            return true;
        }

        public void SimulateResult(int index) => StartCoroutine(After(1f, "OnResultPlaybackEnded", index.ToString()));

        IEnumerator After(float s, string method, string arg)
        {
            yield return new WaitForSecondsRealtime(s);
            Callback(method, arg);
        }

        void Close()
        {
            try { cts?.Cancel(); ws?.Abort(); } catch (Exception) { }
            ws = null;
        }

        void OnDestroy() => Close();
    }
}
#endif
