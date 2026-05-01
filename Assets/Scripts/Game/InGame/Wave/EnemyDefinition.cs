using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemy Definition", fileName = "EnemyDefinition")]
public class EnemyDefinition : ScriptableObject
{
    public string id = "walker";
    public GameObject prefab;
    [Min(1)] public int cost = 1;
    [Header("Wave gating")]
    public int minRound = 1;
    [Header("Group spawn")]
    public Vector2Int groupSizeRange = new Vector2Int(2, 3);
    [Header("Selection weight in mix")]
    public float weight = 1f;
}
