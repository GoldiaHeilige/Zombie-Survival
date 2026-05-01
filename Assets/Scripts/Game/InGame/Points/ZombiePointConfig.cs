// ZombiePointConfig.cs
using UnityEngine;

/// <summary>Config số point cho mỗi zombie (gắn trực tiếp trên prefab).</summary>
[DisallowMultipleComponent]
public class ZombiePointConfig : MonoBehaviour
{
    [Header("Points Reward")]
    [Tooltip("Điểm cộng mỗi lần trúng đạn (không chết).")]
    public int hitPoints = 10;

    [Tooltip("Điểm cộng khi kết liễu zombie này.")]
    public int killPoints = 50;
}
