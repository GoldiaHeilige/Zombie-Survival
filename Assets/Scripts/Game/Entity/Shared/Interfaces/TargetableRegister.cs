using Fusion;

public class TargetableRegister : NetworkBehaviour
{
    ITargetable _t;
    void Awake() => _t = GetComponent<ITargetable>();

    public override void Spawned()
    {
        // Đăng ký theo vai trò Runner (Host/Master), KHÔNG dựa vào HasStateAuthority của object
        var r = Runner;
        if (r == null || r.IsServer || r.IsSharedModeMasterClient)
            TargetService.I?.Register(_t);
    }

    void OnDisable()
    {
        var r = Runner;
        if (r == null || r.IsServer || r.IsSharedModeMasterClient)
            TargetService.I?.Unregister(_t);
    }
}
