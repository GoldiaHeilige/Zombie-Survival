using UnityEngine;

[DisallowMultipleComponent]
public class ZombieAnimatorCtrl : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public ZombieMovement mover;   // để đọc NavMeshAgent + config

    [Header("Animator Params")]
    public string speedParam = "Speed";

    [Header("Smoothing")]
    [Tooltip("Thời gian làm mượt speed (giây)")]
    public float speedSmoothTime = 0.1f;

    int _speedHash;
    Vector3 _lastPos;
    bool _hasLastPos;
    float _currentSpeed;

    void Awake()
    {
        if (!animator)
            animator = GetComponentInChildren<Animator>();

        if (!mover)
            mover = GetComponent<ZombieMovement>();

        _speedHash = Animator.StringToHash(speedParam);
    }

    void OnEnable()
    {
        _hasLastPos = false;
        _currentSpeed = 0f;
    }

    void Update()
    {
        if (!animator) return;
        float rawSpeed = 0f;

        // 1) Nếu mover đang chạy (SP hoặc host) -> dùng ManualVelocity
        if (mover != null && mover.enabled)
        {
            Vector3 v = mover.ManualVelocity;
            v.y = 0f;
            rawSpeed = v.magnitude;
        }
        else
        {
            // 2) MP proxy: mover bị gate tắt -> dùng speed replicate từ net anim
            var netAnim = GetComponent<ZombieNetworkAnimator>();
            if (netAnim != null && netAnim.Object != null && netAnim.Object.IsValid)
            {
                rawSpeed = netAnim.MoveSpeed;
            }
        }



        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, speedSmoothTime));
        _currentSpeed = Mathf.Lerp(_currentSpeed, rawSpeed, t);

        animator.SetFloat(_speedHash, _currentSpeed);
    }

}
