using UnityEngine;
using System;

public class EnemyLifeToken : MonoBehaviour
{
    public Action OnDestroyedOrDisabled;

    bool _counted = false;

    public void Arm(Action onEnd)
    {
        OnDestroyedOrDisabled = onEnd;
        _counted = true;
    }

    void OnDisable()
    {
        if (_counted)
        {
            _counted = false;
            OnDestroyedOrDisabled?.Invoke();
            OnDestroyedOrDisabled = null;
        }
    }

    void OnDestroy()
    {
        if (_counted)
        {
            _counted = false;
            OnDestroyedOrDisabled?.Invoke();
            OnDestroyedOrDisabled = null;
        }
    }
}
