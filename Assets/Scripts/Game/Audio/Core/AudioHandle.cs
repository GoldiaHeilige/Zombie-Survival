// AudioHandle.cs
using UnityEngine;

namespace TT
{
    /// <summary>
    /// Handle nhẹ để tham chiếu tới 1 lần phát audio cụ thể.
    /// Cho phép Stop / Fade mà không phải đụng trực tiếp AudioSource.
    /// </summary>
    public struct AudioHandle
    {
        internal AudioSource source;
        internal int token;

        public bool IsValid => source != null;
    }
}
