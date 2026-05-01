using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quản lý bật/tắt nhóm wrapper theo chế độ game.
/// Không còn spawn CorePrefab – Core luôn nằm sẵn trong PlayerShell.
/// </summary>
public class CoreWrapperActivator : MonoBehaviour
{
    [Header("Wrapper roots (child GameObjects)")]
    [SerializeField] private GameObject singleRoot; // ví dụ: child "Wrappers_Single"
    [SerializeField] private GameObject multiRoot;  // ví dụ: child "Wrappers_Multi"

    void OnEnable()
    {
        // Tự kiểm tra runner sau khi spawn
        StartCoroutine(Co_ActivateAfterSpawn());
    }

    IEnumerator Co_ActivateAfterSpawn()
    {
        yield return null; // chờ 1 frame để NetworkRunner tồn tại
        bool isMultiplayer = Object.FindFirstObjectByType<Fusion.NetworkRunner>() != null;
        AttachWrappersForMode(isMultiplayer);
    }


    /// <summary>
    /// Bật wrapper theo chế độ, được gọi từ Spawner hoặc Selector.
    /// </summary>
    public void AttachWrappersForMode(bool isMultiplayer)
    {
        if (singleRoot) singleRoot.SetActive(!isMultiplayer);
        if (multiRoot) multiRoot.SetActive(isMultiplayer);

        Debug.Log($"[CoreWrapperActivator] Mode={(isMultiplayer ? "MULTI" : "SINGLE")} → " +
                  $"singleRoot={singleRoot?.name} active={!isMultiplayer}, " +
                  $"multiRoot={multiRoot?.name} active={isMultiplayer}");
    }
}
