using UnityEngine;

public class AIPortHub : MonoBehaviour
{
    public static AIPortHub I { get; private set; }

    [Header("Ports (assigned by Impl at runtime)")]
    public ISpawnPort Spawn;
    public IRandomPort Random;
    public ITargetQuery Target;
    public IGameStartPort GameStart;
    public IDeathEvents DeathEvents;

    void Awake()
    {
        if (I != null && I != this)
        {
            Debug.LogWarning("[AIPortHub] Duplicate hub, destroying this.", this);
            Destroy(gameObject);
            return;
        }
        I = this;
     //   DontDestroyOnLoad(gameObject); // nếu bạn chuyển scene gameplay nhiều lần thì giữ; nếu không cần thì bỏ
    }
}
