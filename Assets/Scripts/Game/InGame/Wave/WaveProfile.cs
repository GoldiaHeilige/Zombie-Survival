using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Wave Profile", fileName = "WaveProfile")]
public class WaveProfile : ScriptableObject
{
    [Header("Budget & Concurrency")]
    [Min(1)] public int budgetPoints = 8;          // tổng điểm để tiêu trong wave
    [Min(1)] public int concurrencyCap = 5;        // alive tối đa đồng thời
    [Header("Bursting")]
    [Min(1)] public int spawnBurst = 3;            // mỗi đợt bơm ra tối đa bao nhiêu con
    [Min(0f)] public float interBurstDelay = 2f;   // delay giữa các burst
    [Header("Allowed enemy types for this wave")]
    public List<EnemyDefinition> allowTypes = new List<EnemyDefinition>();
}
