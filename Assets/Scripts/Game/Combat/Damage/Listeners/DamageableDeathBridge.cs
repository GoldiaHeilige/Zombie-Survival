// DamageableDeathBridge.cs
using UnityEngine;

[DisallowMultipleComponent]
public class DamageableDeathBridge : MonoBehaviour
{
    DamageableHealth _hp;
    EnemySpawnHandle _ticket;

    void Awake()
    {
        _hp = GetComponent<DamageableHealth>();
        _ticket = GetComponent<EnemySpawnHandle>();
        if (_hp != null) _hp.OnDeathLocal += OnDeathLocal;
    }

    void OnDestroy()
    {
        if (_hp != null) _hp.OnDeathLocal -= OnDeathLocal;
    }

    void OnDeathLocal(DamageEvent e, DamageResult r)
    {
        if (_ticket == null) _ticket = GetComponent<EnemySpawnHandle>();
        _ticket?.ReportDeath(); // giữ hệ đếm
    }
}
