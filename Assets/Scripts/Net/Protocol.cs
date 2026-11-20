using System;

namespace BrokenTelephone
{
    // DTOs for the JSON protocol. JsonUtility is used (not Newtonsoft): no reflection-heavy
    // dependency, fast, and it survives High managed stripping on WebGL.

    [Serializable] public class Envelope { public string t; }

    [Serializable] public class WelcomeMsg { public string deviceId; public string name; }
    [Serializable] public class ErrorMsg { public string msg; }

    [Serializable]
    public class PlayerInfo
    {
        public string id;
        public string name;
        public bool bot;
        public bool online;
    }

    [Serializable]
    public class RoomMsg
    {
        public string code;
        public string hostId;
        public string phase;          // lobby | game | results
        public string lang;           // room language: en | hi | kn | ml (headline region + speech)
        public int minPlayers;
        public bool bots;             // host toggle: fill empty seats with bots
        public int recordMs;
        public PlayerInfo[] players;
        public PlayerInfo[] lineup;
    }

    [Serializable]
    public class TurnMsg
    {
        public string phase;          // loading | read | listen | record | bot | paused
        public int index;
        public int total;
        public string playerId;
        public int round;
        public int remainingMs;
        public string sentence;       // only for the reader, only during 'read'
        public bool played;           // listener already used their single playback (survives refresh)
    }

    [Serializable] public class NoticeMsg { public string kind; public string playerId; public string msg; }
    [Serializable] public class TextAtIndexMsg { public int index; public string text; }

    [Serializable]
    public class ResultEntry
    {
        public int index;
        public string playerId;
        public string name;
        public bool bot;
        public string heard;
        public string said;
        public int score;             // 0..100, -1 = not scorable (no transcript)
        public bool hasAudio;
    }

    [Serializable]
    public class RankEntry
    {
        public string playerId;
        public string name;
        public bool bot;
        public int score;
        public int rank;              // 0 for bots
    }

    [Serializable]
    public class ResultsMsg
    {
        public string original;
        public string final;
        public int overall;
        public ResultEntry[] entries;
        public RankEntry[] ranking;
    }

    [Serializable] public class RecordingResult { public string transcript; public bool voiced; }

    // Outgoing
    [Serializable] public class HelloOut { public string t = "hello"; public string deviceId; public string name; }
    [Serializable] public class JoinOut { public string t = "join"; public string code; }
    [Serializable] public class RenameOut { public string t = "rename"; public string name; }
    [Serializable] public class IndexOut { public string t; public int index; }
}
