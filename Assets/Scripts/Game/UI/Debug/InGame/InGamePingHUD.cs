using System;
using System.Collections;
using System.Linq;
using Fusion;
using TMPro;
using UnityEngine;

namespace TT.UI
{
    public class InGamePingHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text txtPing;
        [SerializeField] private float refreshSec = 0.35f;

        private NetworkRunner _runner;
        private Coroutine _tick;

        private void OnEnable()
        {
            if (_tick != null) StopCoroutine(_tick);
            _tick = StartCoroutine(CoTick());
        }

        private void OnDisable()
        {
            if (_tick != null) StopCoroutine(_tick);
            _tick = null;
        }

        private IEnumerator CoTick()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(0.05f, refreshSec));

            while (true)
            {
                if (!txtPing)
                {
                    yield return wait;
                    continue;
                }

                if (_runner == null)
                    _runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);

                // Single / chưa có runner
                if (_runner == null || !_runner.IsRunning || GameSession.Mode == AppPlayMode.Single)
                {
                    txtPing.text = "Ping: -- ms";
                    txtPing.color = Color.white;
                    yield return wait;
                    continue;
                }

                int? ping = TryGetPingMs(_runner, _runner.LocalPlayer);
                if (ping.HasValue)
                {
                    txtPing.text = $"Ping: {ping.Value} ms";
                    txtPing.color = GetPingColor(ping.Value);
                }
                else
                {
                    txtPing.text = "Ping: -- ms";
                    txtPing.color = Color.white;
                }

                yield return wait;
            }
        }

        private static Color GetPingColor(int pingMs)
        {
            // <=90 xanh, 91-138 vàng, >=139 đỏ
            if (pingMs <= 90) return new Color32(60, 220, 90, 255);
            if (pingMs <= 138) return new Color32(240, 200, 60, 255);
            return new Color32(240, 80, 80, 255);
        }

        // reflection để tương thích nhiều bản Fusion (y như lobby)
        private static int? TryGetPingMs(NetworkRunner r, PlayerRef p)
        {
            if (r == null) return null;

            try
            {
                var m = r.GetType().GetMethod("GetPlayerRtt", new[] { typeof(PlayerRef) });
                if (m != null)
                {
                    object v = m.Invoke(r, new object[] { p });
                    if (v is float f) return Mathf.Clamp(Mathf.RoundToInt(f * 1000f), 0, 9999);
                    if (v is double d) return Mathf.Clamp((int)Math.Round(d * 1000.0), 0, 9999);
                }

                var m2 = r.GetType().GetMethods()
                    .FirstOrDefault(x => x.Name.IndexOf("Rtt", StringComparison.OrdinalIgnoreCase) >= 0
                                         && x.GetParameters().Length == 1
                                         && x.GetParameters()[0].ParameterType == typeof(PlayerRef));
                if (m2 != null)
                {
                    object v = m2.Invoke(r, new object[] { p });
                    if (v is float f2) return Mathf.Clamp(Mathf.RoundToInt(f2 * 1000f), 0, 9999);
                    if (v is double d2) return Mathf.Clamp((int)Math.Round(d2 * 1000.0), 0, 9999);
                }
            }
            catch { }

            return null;
        }
    }
}
