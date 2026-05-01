using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ánh xạ giữa WeaponDef.weaponId (string) ↔ int key (hash).
/// Client/Server đều dùng được. Tải lười (lazy) lúc đầu.
/// </summary>
public static class WeaponIdRegistry
{
    private static readonly Dictionary<string, int> _idToKey = new();
    private static readonly Dictionary<int, WeaponDef> _keyToDef = new();
    private static bool _initialized;

    private static void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;

        // Quét tất cả WeaponDef đang có trong memory (Project/Resources).
        var defs = Resources.FindObjectsOfTypeAll<WeaponDef>();
        foreach (var def in defs)
        {
            if (!def || string.IsNullOrEmpty(def.weaponId)) continue;
            var key = Animator.StringToHash(def.weaponId); // ổn định theo chuỗi
            if (!_idToKey.ContainsKey(def.weaponId)) _idToKey.Add(def.weaponId, key);
            if (!_keyToDef.ContainsKey(key)) _keyToDef.Add(key, def);
        }
    }

    public static int GetKey(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        EnsureInit();
        if (_idToKey.TryGetValue(id, out var key)) return key;
        // Nếu chưa thấy (asset load sau), tạo key mới và chèn khi có Def
        key = Animator.StringToHash(id);
        _idToKey[id] = key;
        return key;
    }

    public static WeaponDef GetDef(int key)
    {
        if (key == 0) return null;
        EnsureInit();
        if (_keyToDef.TryGetValue(key, out var def) && def) return def;

        // Nếu chưa khớp, thử quét lại (khi asset vừa load xong)
        _initialized = false;
        EnsureInit();
        _keyToDef.TryGetValue(key, out def);
        return def;
    }
}
