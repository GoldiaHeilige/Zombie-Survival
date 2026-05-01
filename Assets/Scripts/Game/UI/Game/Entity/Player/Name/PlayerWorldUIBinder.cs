using UnityEngine;
using Fusion;
using System.Collections;
using UnityEngine.Rendering;

public class PlayerWorldUIBinder : MonoBehaviour
{
    [Header("Assign trong prefab PlayerRefs")]
    public Transform worldUIAnchor;

    [Header("Assign trong Project")]
    public GameObject worldUIPrefab;

    private FusionNetBridge _bridge;
    private GameObject _uiInstance;
    private PlayerLifeController _life;

    void Awake()
    {
        _bridge = GetComponentInParent<FusionNetBridge>(true);
        _life = GetComponentInParent<PlayerLifeController>(true);
    }

    void Start()
    {
        // SP không có WorldUI name
        if (GameSession.Mode == AppPlayMode.Single)
            return;

        if (!_bridge)
        {
            Debug.LogWarning("[WorldUI] Không tìm thấy FusionNetBridge.");
            return;
        }

        if (!worldUIPrefab)
        {
            Debug.LogError("[WorldUI] worldUIPrefab chưa được assign!");
            return;
        }

        StartCoroutine(WaitForNameThenSpawn());
    }

    private IEnumerator WaitForNameThenSpawn()
    {
        // CHỜ NetworkObject tồn tại & valid
        while (_bridge && (_bridge.Object == null || !_bridge.Object.IsValid))
            yield return null;

        if (!_bridge || _bridge.Object == null || !_bridge.Object.IsValid)
            yield break;

        // CHỜ PlayerRefs
        while (_bridge && !_bridge.GetComponentInChildren<PlayerRefs>(true))
            yield return null;

        if (!_bridge)
            yield break;

        // CHỜ worldUIAnchor
        while (worldUIAnchor == null)
            yield return null;

        // CHỜ DisplayName sync an toàn
        string cachedName = null;

        while (true)
        {
            if (_bridge == null || _bridge.Object == null || !_bridge.Object.IsValid)
                yield break; // player bị despawn giữa chừng → thoát

            try
            {
                if (_bridge.DisplayName.Length > 0)
                {
                    cachedName = _bridge.DisplayName.ToString();
                    break;
                }
            }
            catch (System.InvalidOperationException)
            {
                // Object đã despawn hoặc chưa spawn đúng cách → dừng luôn
                yield break;
            }

            yield return null;
        }

        if (string.IsNullOrEmpty(cachedName))
            yield break;

        SpawnUI(cachedName);
    }


    private void SpawnUI(string name)
    {
        if (_uiInstance != null) return;

        // instantiate
        _uiInstance = Instantiate(worldUIPrefab);
        _uiInstance.name = "WorldUI_" + name;

        // gán text
        var text = _uiInstance.GetComponentInChildren<TMPro.TMP_Text>();
        if (text != null)
        {
            text.text = name;

            // Áp dụng material luôn hiển thị
/*            ApplyAlwaysOnTopMaterial(text);*/
        }

        if (_bridge.Object != null && _bridge.Object.IsValid)
        {
            int pid = _bridge.Object.InputAuthority.PlayerId;
            text.color = PlayerColorPalette.GetFromFusionPlayerId(pid);
        }


        /*        var bg = _uiInstance.GetComponentInChildren<UnityEngine.UI.Image>();
                if (bg != null) ApplyOverlayForImage(bg);*/

        // follow logic
        var follow = _uiInstance.GetComponent<WorldUIFollow>();
        if (follow != null)
        {
            follow.target = worldUIAnchor;
            follow.Init(_life);      // 🔴 pass Life vào để WorldUIFollow tự quản lý icon Downed
        }

        // Ẩn UI của chính player local
        if (_bridge.Object != null && _bridge.Object.HasInputAuthority)
            _uiInstance.SetActive(false);

        // đảm bảo scale chuẩn (Canvas world-space thường bug scale)
        _uiInstance.transform.localScale = Vector3.one * 0.01f;
    }
    void OnDestroy()
    {
        if (_uiInstance)
            Destroy(_uiInstance);
    }
}
