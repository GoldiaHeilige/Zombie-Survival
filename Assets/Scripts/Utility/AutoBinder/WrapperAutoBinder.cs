// WrapperAutoBinder.cs
using System.Reflection;
using UnityEngine;

public class WrapperAutoBinder : MonoBehaviour
{
    [SerializeField] Transform root;   // để trống = transform.root

    void Awake()
    {
        if (!root) root = transform.root;

        var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            var t = b.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = t.GetFields(flags);

            foreach (var f in fields)
            {
                if (f.IsDefined(typeof(AutoBindInParentAttribute), inherit: true))
                {
                    // chỉ bind khi còn null/chưa set
                    var current = f.GetValue(b);
                    if (current != null) continue;

                    var comp = root.GetComponentInChildren(f.FieldType, includeInactive: true)
                              ?? root.GetComponentInParent(f.FieldType);
                    if (comp != null) f.SetValue(b, comp);
                }
            }
        }
        // Có thể Destroy(this) nếu bạn muốn dọn sạch sau khi bind
        // Destroy(this);
    }
}
