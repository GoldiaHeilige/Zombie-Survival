// Assets/Scripts/AI/Zombie/Attack/ZombieAttackSet.cs
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "AI/Zombie/Attack Set", fileName = "AS_NewSet")]
public class ZombieAttackSet : ScriptableObject
{
    public AttackSelectMode selectionMode = AttackSelectMode.HighestPriority;
    public float globalCooldown = 0f; // nếu muốn delay chung giữa các chiêu
    public ZombieAttackProfile[] profiles = new ZombieAttackProfile[0];

    public bool HasProfiles => profiles != null && profiles.Length > 0;

    public ZombieAttackProfile[] SortedByPriorityDesc()
    {
        if (!HasProfiles) return new ZombieAttackProfile[0];
        return profiles.Where(p => p != null).OrderByDescending(p => p.priority).ToArray();
    }
}
