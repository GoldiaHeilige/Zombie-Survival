using System.Collections.Generic;
using UnityEngine;

public class MovementModifierManager
{
    readonly List<MovementModifier> _active = new();

    public void Apply(MovementModifier mod) => _active.Add(mod);

    public void RemoveById(string id) => _active.RemoveAll(m => m.id == id);

    public void RemoveBySource(string source) => _active.RemoveAll(m => m.source == source);

    // gọi mỗi frame từ controller
    public void Tick(float dt)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var m = _active[i];
            if (m.duration >= 0f)
            {
                m.duration -= dt;
                if (m.duration <= 0f) _active.RemoveAt(i);
            }
        }
    }

    public void BakeInto(MovementStats stats, MovementConfig cfg)
    {
        stats.LoadFromConfig(cfg);
        foreach (var m in _active)
            if (m.entries != null)
                foreach (var e in m.entries) stats.Apply(e);
    }
}
