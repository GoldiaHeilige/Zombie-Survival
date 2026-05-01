// PerkDefinition.cs
using UnityEngine;

namespace TT
{
    [CreateAssetMenu(menuName = "TT/Perks/Perk Definition", fileName = "Perk_")]
    public class PerkDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique id (ví dụ: juggernog, speed_cola...)")]
        public string perkId;

        public string displayName;

        [TextArea]
        public string description;

        [Header("Economy")]
        public int cost = 2000;

        [Min(1)]
        public int maxStacks = 1;

        [Header("UI")]
        public Sprite icon;

        [Header("Effects")]
        [Tooltip("List effect modules (có thể để 1 cái).")]
        public PerkEffectSO[] effects;

        public bool IsValid => !string.IsNullOrWhiteSpace(perkId);
    }
}
