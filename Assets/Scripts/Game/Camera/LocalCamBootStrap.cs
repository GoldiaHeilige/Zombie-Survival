using System.Collections;
using System.Linq;
using Fusion;
using UnityEngine;
using Unity.Cinemachine;

public class LocalCamBootstrap : MonoBehaviour
{
    [Header("Refs (optional)")]
    [SerializeField] CinemachineCamera vcam;   // có thể để trống, script sẽ tự tìm
    [SerializeField] string cameraRootName = "CameraRoot";
    [SerializeField] float retryInterval = 0.1f;

    void Start()
    {
        if (!vcam)
        {
            vcam = GetComponentsInChildren<CinemachineCamera>(true).FirstOrDefault();
        }

        if (!vcam)
        {
            Debug.LogError("[LocalCamBootstrap] Không thấy CinemachineVirtualCamera trong _CamRef!");
            return;
        }

        StartCoroutine(BindWhenReady());
    }

    IEnumerator BindWhenReady()
    {
        Transform followTarget = null;

        // 1) Chờ PlayerRefs xuất hiện
        PlayerRefs refs = null;
        while (refs == null)
        {
            var all = Object.FindObjectsByType<PlayerRefs>(FindObjectsSortMode.None);

            if (all != null && all.Length > 0)
            {
                refs = null;
                foreach (var r in all)
                {
                    var no = r.GetComponentInParent<NetworkObject>();
                    if (no != null && no.HasInputAuthority)
                    {
                        refs = r;
                        break;
                    }
                }
            }

            if (refs == null) yield return new WaitForSeconds(retryInterval);
        }

        // 2) Lấy camFollowTarget nếu có, nếu không thì tìm Transform tên "CameraRoot" trong player
        float waited = 0f;
        while (followTarget == null && waited < 5f) // timeout nhẹ để tránh treo
        {
            if (refs.camFollowTarget) followTarget = refs.camFollowTarget;
            else
            {
                // Tìm theo tên trong nhánh player
                var t = refs.transform.GetComponentsInChildren<Transform>(true)
                                       .FirstOrDefault(x => x.name == cameraRootName);
                if (t) followTarget = t;
            }

            if (followTarget == null)
            {
                waited += retryInterval;
                yield return new WaitForSeconds(retryInterval);
            }
        }

        if (!followTarget)
        {
            Debug.LogError($"[LocalCamBootstrap] Không tìm thấy '{cameraRootName}' hoặc camFollowTarget trong Player.");
            yield break;
        }

        // 3) Gán Follow/LookAt
        vcam.Follow = followTarget;
     //   vcam.LookAt = followTarget;

        // Log nhẹ để biết đã bind
        Debug.Log($"[LocalCamBootstrap] Bound VCam → {followTarget.name}");
    }
}
