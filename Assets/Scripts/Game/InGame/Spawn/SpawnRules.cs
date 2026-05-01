using UnityEngine;

[CreateAssetMenu(menuName = "Game/Spawn Rules", fileName = "SpawnRules")]
public class SpawnRules : ScriptableObject
{
    [Header("Distance")]
    [Min(0f)] public float minPlayerDistance = 18f;
    [Min(0f)] public float maxPlayerDistance = 60f;

    [Header("NavMesh sample")]
    [Min(0.1f)] public float sampleRadius = 1.2f;
    [Min(1)] public int maxSampleTries = 6;

    [Header("Visibility checks")]
    public bool checkFOV = true;
    public bool checkLOS = true;

    [Tooltip("Góc nhìn của camera người chơi. 90 = góc nón 90°, tương đương FOV ~90.")]
    [Range(30f, 140f)] public float cameraConeAngle = 90f;

    [Tooltip("Viền an toàn trên viewport (0..0.45). 0.1 = bỏ mép 10% bốn phía.")]
    [Range(0f, 0.45f)] public float viewportEdgePadding = 0.08f;

    [Header("Separation (chống chồng chéo)")]
    [Tooltip("Khoảng cách tối thiểu giữa các điểm spawn trong cùng một burst.")]
    [Min(0f)] public float minSeparationBetweenSpawns = 3f;

    [Header("Physics masks")]
    public LayerMask losObstructionMask = ~0; // tường/đồ che khuất
    public LayerMask groundMask = ~0;         // dùng nếu cần ray xuống đất (tùy bạn bật)

    [Header("Debug")]
    public bool verboseLogs = false;
}
