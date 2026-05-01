public interface IHealthState
{
    float Current { get; }
    float Max { get; }
    bool IsDead { get; }
    bool IsDowned { get; }  // nếu chưa có downed, cứ trả false

    event System.Action<float, float> OnHealthChanged; // current, max
    event System.Action OnDeath;
    event System.Action OnRevive; // để dành
}
