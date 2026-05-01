/// Phát trạng thái để UI/Debug GUI subscribe (client không tự tính).
/// Bạn có thể map sang hệ Observer/Topics sẵn có.
public interface IStateBroadcaster
{
    void EmitRoundChanged(int roundIndex);
    void EmitAliveChanged(int alive);
    void EmitBudgetChanged(int spentBudget);

    /// Ghi chú/nhật ký ngắn cho wave/spawn (ví dụ jitter, nguồn spawn đang active, v.v.)
    void EmitNote(string topic, string message);
}
