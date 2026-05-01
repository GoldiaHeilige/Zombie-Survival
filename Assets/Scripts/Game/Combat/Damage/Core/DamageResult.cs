public struct DamageResult
{
    public float finalDamage;
    public bool isApplied;        // có thực sự trừ máu không
    public bool isFatal;          // nạn nhân chết sau đòn này?
    public float remainingHealth; // còn lại bao nhiêu

    public override string ToString()
        => $"DamageResult(applied={isApplied}, final={finalDamage:0.##}, fatal={isFatal}, remain={remainingHealth:0.##})";
}
