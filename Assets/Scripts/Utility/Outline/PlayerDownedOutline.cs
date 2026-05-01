using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDownedOutlineToggle : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerLifeController life;
    [SerializeField] private PlayerAppearance appearance;

    [Header("Runtime")]
    [Tooltip("Outlines nằm trong skin instance (world prefab).")]
    [SerializeField] private Outline[] outlines;

    void Awake()
    {
        if (!life) life = GetComponentInParent<PlayerLifeController>();
        if (!appearance) appearance = GetComponentInParent<PlayerAppearance>();

        // Nếu skin đã spawn trước khi script enable, bind luôn
        RebindFromCurrentSkin();

        // Apply state hiện tại (tránh start sai)
        Apply(life ? life.state : LifeState.Alive);
    }

    void OnEnable()
    {
        if (appearance != null)
            appearance.OnSkinSpawned += OnSkinSpawned;

        if (life != null)
        {
            life.OnDowned += OnStateChanged;
            life.OnRevived += OnStateChanged;
            life.OnRespawned += OnStateChanged;
            life.OnDead += OnStateChanged;

            Apply(life.state);
        }

        // lại 1 lần nữa cho chắc (trường hợp spawn skin giữa Awake/OnEnable)
        RebindFromCurrentSkin();
    }

    void OnDisable()
    {
        if (appearance != null)
            appearance.OnSkinSpawned -= OnSkinSpawned;

        if (life != null)
        {
            life.OnDowned -= OnStateChanged;
            life.OnRevived -= OnStateChanged;
            life.OnRespawned -= OnStateChanged;
            life.OnDead -= OnStateChanged;
        }

        SetAll(false);
    }

    void OnSkinSpawned(GameObject skinInstance)
    {
        // tắt outline cũ để không “kẹt” nếu đổi skin
        SetAll(false);

        if (!skinInstance)
        {
            outlines = null;
            return;
        }

        outlines = skinInstance.GetComponentsInChildren<Outline>(true);

        // Apply lại theo state hiện tại
        Apply(life ? life.state : LifeState.Alive);
    }

    void OnStateChanged(PlayerLifeController who)
    {
        Apply(who != null ? who.state : LifeState.Alive);
    }

    void RebindFromCurrentSkin()
    {
        if (appearance == null) return;

        var skin = appearance.CurrentSkinInstance;
        if (!skin) return;

        outlines = skin.GetComponentsInChildren<Outline>(true);
    }

    void Apply(LifeState state)
    {
        bool on = (state == LifeState.Downed);
        SetAll(on);
    }

    void SetAll(bool on)
    {
        if (outlines == null) return;
        for (int i = 0; i < outlines.Length; i++)
            if (outlines[i]) outlines[i].enabled = on;
    }
}
