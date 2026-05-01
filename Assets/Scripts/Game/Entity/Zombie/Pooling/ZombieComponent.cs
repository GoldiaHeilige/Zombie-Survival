using UnityEngine;

public class ZombieComponent : MonoBehaviour
{
    [Tooltip("ID pool tương ứng (ví dụ 'walker1')")]
    public string poolId;

    private void OnDisable() { }
}
