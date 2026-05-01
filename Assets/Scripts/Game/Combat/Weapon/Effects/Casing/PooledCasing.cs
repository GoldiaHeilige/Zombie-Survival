using UnityEngine;

public class PooledCasing : MonoBehaviour
{
    public GameObject Prefab { get; private set; }

    Rigidbody _rb;
    Collider[] _cols;
    CasingImpactSfx _impact; // script FP impact bạn đã tạo

    float _returnAt;
    bool _scheduled;

    public void SetPrefab(GameObject prefab) => Prefab = prefab;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _cols = GetComponentsInChildren<Collider>(true);
        _impact = GetComponent<CasingImpactSfx>();
    }

    public void OnRented()
    {
        // reset physics
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.Sleep();
            _rb.WakeUp();
        }

        // reset collider enabled (nếu trước đó disable)
        if (_cols != null)
        {
            foreach (var c in _cols)
                if (c) c.enabled = true;
        }

        _scheduled = false;
    }

    public void OnReturned()
    {
        // tránh casing đang bay nhưng bị “giữ lại”
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.Sleep();
        }

        // tắt impact state nếu có
        if (_impact != null)
            _impact.ResetState();
    }

    public void ScheduleReturn(float seconds)
    {
        _returnAt = Time.time + Mathf.Max(0.05f, seconds);
        _scheduled = true;
    }

    void Update()
    {
        if (_scheduled && Time.time >= _returnAt)
        {
            _scheduled = false;
            CasingPool.Instance?.Return(this);
        }
    }
}
