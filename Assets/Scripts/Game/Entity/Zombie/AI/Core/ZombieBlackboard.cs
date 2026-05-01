using UnityEngine;
using System.Diagnostics; // để dùng StackTrace

[DisallowMultipleComponent]
public class ZombieBlackboard : MonoBehaviour, IPoolable
{
    [Header("Target (runtime)")]
    [SerializeField] Transform _target;   // backing field thực
    public float distanceToTarget;
    public bool hasLOS;

    [Header("Tuning")]
    public float awarenessRange = 20f;   // tầm “nhìn/nghe”
    public float attackRange = 1.6f;     // tầm cận chiến (chưa gây dmg giai đoạn này)

    [Header("Movement")]
    public float thinkIntervalNear = 0.1f;  // tick nhanh khi gần
    public float thinkIntervalFar = 0.3f;   // tick chậm khi xa

    [HideInInspector] public float lastSawTargetTime = -999f;

    // ====== PROPERTY CÓ LOG ======

    public Transform target
    {
        get => _target;
        set
        {
            if (_target == value) return; // không đổi thì thôi

            string oldName = _target ? _target.name : "<null>";
            string newName = value ? value.name : "<null>";

/*            UnityEngine.Debug.Log(
                $"[BB] {name} target {oldName} -> {newName}\n" +
                new StackTrace(true),
                this
            );*/

            _target = value;
        }
    }

    public bool HasTarget() => _target != null;

    public void OnSpawned()
    {
        _target = null;
        distanceToTarget = 0f;
        hasLOS = false;
        lastSawTargetTime = -999f;
    }

    public void OnDespawned()
    {
        _target = null;
    }
}
