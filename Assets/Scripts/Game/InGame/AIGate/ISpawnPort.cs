using UnityEngine;

/// Bridge spawn/despawn giữa SP và MP.
/// SPImpl: dùng ZombiePoolManager. MPImpl: Host spawn/despawn network object; Client no-op.
public interface ISpawnPort
{
    /// Yêu cầu spawn 1 enemy theo định nghĩa và transform mong muốn.
    /// Trả về GameObject được spawn (có thể null ở client/no-op).
    GameObject Spawn(EnemyDefinition def, Vector3 position, Quaternion rotation);

    /// Yêu cầu despawn 1 enemy đã spawn.
    void Despawn(GameObject instance);
}
