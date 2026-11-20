using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BrokenTelephone
{
    /// <summary>
    /// Thin facade over the browser plugin (BrokenTelephone.jslib).
    /// WebGL player: every call is a direct JS interop call, results come back via SendMessage on the
    /// receiver GameObject. Editor / other platforms: <see cref="EditorBackend"/> emulates the same
    /// callbacks (real WebSocket, simulated mic/STT) so the full flow is testable in Play mode.
    /// </summary>
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void BT_Init(string goName);
        [DllImport("__Internal")] static extern string BT_GetDeviceId();
        [DllImport("__Internal")] static extern string BT_GetServerUrl();
        [DllImport("__Internal")] static extern string BT_GetRoomFromUrl();
        [DllImport("__Internal")] static extern string BT_GetShareLink(string code);
        [DllImport("__Internal")] static extern void BT_CopyText(string text);
        [DllImport("__Internal")] static extern void BT_Connect(string url);
        [DllImport("__Internal")] static extern void BT_Send(string json);
        [DllImport("__Internal")] static extern int BT_IsSttSupported();
        [DllImport("__Internal")] static extern void BT_RequestMic();
        [DllImport("__Internal")] static extern void BT_StartRecording(int ms, string lang);
        [DllImport("__Internal")] static extern void BT_StopRecording();
        [DllImport("__Internal")] static extern int BT_PlayTurnAudio();
        [DllImport("__Internal")] static extern void BT_SetTurnTts(string text, string lang);
        [DllImport("__Internal")] static extern void BT_ClearTurnMedia();
        [DllImport("__Internal")] static extern int BT_PlayResult(int index);
        [DllImport("__Internal")] static extern void BT_SpeakResult(int index, string text, string lang);
        [DllImport("__Internal")] static extern void BT_StopAudio();
        [DllImport("__Internal")] static extern void BT_ClearResults();
        [DllImport("__Internal")] static extern string BT_GetBrowserLang();
        [DllImport("__Internal")] static extern void BT_LoadFonts();
        [DllImport("__Internal")] static extern void BT_LoadKannadaFont();
        [DllImport("__Internal")] static extern int BT_TextRender(string text, int fontPx, bool bold, int maxW, bool wrap, int align);
        [DllImport("__Internal")] static extern void BT_TextCopy(byte[] dst);

        public static void Init(GameObject receiver) => BT_Init(receiver.name);
        public static string DeviceId() => BT_GetDeviceId();
        public static string ServerUrl() => BT_GetServerUrl();
        public static string RoomFromUrl() => BT_GetRoomFromUrl();
        public static string ShareLink(string code) => BT_GetShareLink(code);
        public static void Copy(string text) => BT_CopyText(text);
        public static void Connect(string url) => BT_Connect(url);
        public static void Send(string json) => BT_Send(json);
        public static bool SttSupported => BT_IsSttSupported() == 1;
        public static void RequestMic() => BT_RequestMic();
        public static void StartRecording(int ms, string lang) => BT_StartRecording(ms, lang);
        public static void StopRecording() => BT_StopRecording();
        public static bool PlayTurnAudio() => BT_PlayTurnAudio() == 1;
        public static void SetTurnTts(string text, string lang) => BT_SetTurnTts(text, lang);
        public static void ClearTurnMedia() => BT_ClearTurnMedia();
        public static bool PlayResult(int index) => BT_PlayResult(index) == 1;
        public static void SpeakResult(int index, string text, string lang) => BT_SpeakResult(index, text, lang);
        public static void StopAudio() => BT_StopAudio();
        public static void ClearResults() => BT_ClearResults();
        public static string BrowserLang() => BT_GetBrowserLang();
        public static void LoadFonts() => BT_LoadFonts();
        public static void LoadKannadaFont() => BT_LoadKannadaFont();
        public static int TextRender(string text, int fontPx, bool bold, int maxW, bool wrap, int align) => BT_TextRender(text, fontPx, bold, maxW, wrap, align);
        public static void TextCopy(byte[] dst) => BT_TextCopy(dst);
#else
        static EditorBackend backend;

        public static void Init(GameObject receiver) => backend = receiver.AddComponent<EditorBackend>();
        public static string DeviceId()
        {
            // Separate id per Editor instance (handy with ParrelSync / multiple editors).
            const string key = "bt_device_id";
            var id = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(id)) { id = "editor-" + Guid.NewGuid().ToString("N"); PlayerPrefs.SetString(key, id); }
            return id;
        }
        public static string ServerUrl() => "ws://localhost:8080/ws";
        public static string RoomFromUrl() => "";
        public static string ShareLink(string code) => "http://localhost:8080/?room=" + code;
        public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;
        public static void Connect(string url) => backend.Connect(url);
        public static void Send(string json) => backend.Send(json);
        public static bool SttSupported => true;
        public static void RequestMic() => backend.Callback("OnMicResult", "ok");
        public static void StartRecording(int ms, string lang) => backend.SimulateRecording(ms);
        public static void StopRecording() => backend.FinishRecording();
        public static bool PlayTurnAudio() => backend.SimulatePlayback("OnPlaybackEnded", "");
        public static void SetTurnTts(string text, string lang) => backend.hasTurnMedia = true;
        public static void ClearTurnMedia() => backend.hasTurnMedia = false;
        public static bool PlayResult(int index) { backend.SimulateResult(index); return true; }
        public static void SpeakResult(int index, string text, string lang) => backend.SimulateResult(index);
        public static void StopAudio() { }
        public static void ClearResults() { }
        public static string BrowserLang() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        public static void LoadFonts() { }
        public static void LoadKannadaFont() { }
#endif
    }
}
