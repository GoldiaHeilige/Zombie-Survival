using System;

/// Nhận thông báo tử vong của enemy để Director/Spawner trừ alive, despawn, v.v.
/// Host là nơi xử lý thật sự; client có thể chỉ phát sự kiện UI.
public interface IDeathEvents
{
    /// Phát khi 1 enemy chết (hoặc bị disable theo vòng đời).
    event Action<EnemyLifeToken> OnEnemyDied;
}
