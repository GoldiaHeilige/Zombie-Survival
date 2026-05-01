// NetworkTopics.cs
using System;

namespace TT
{
    public enum NetDisconnectKind
    {
        Unknown,
        HostLeft,
        ConnectionLost,
        Kicked,
        Rejected
    }

    /// <summary>Dữ liệu gửi lên Observer khi bị disconnect.</summary>
    public struct NetDisconnectInfo
    {
        public NetDisconnectKind kind;
        public string reasonText;
    }

    /// <summary>Tên topic chuẩn cho các event network.</summary>
    public static class NetworkTopics
    {
        /// <summary>
        /// Bắn khi session bị ngắt (host leave, mất mạng, bị đá...).
        /// Data: NetDisconnectInfo
        /// </summary>
        public const string Disconnected = "net.disconnected";
    }
}
