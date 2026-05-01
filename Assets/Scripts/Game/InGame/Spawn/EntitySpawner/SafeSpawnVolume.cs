using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SafeSpawnVolume : MonoBehaviour
{
    public float enableSeconds = 2f;
    public LayerMask zombieLayer;
    public float pushOutForce = 6f;

    Collider _col;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
        gameObject.SetActive(false);
    }

    public void EnableFor(float seconds)
    {
        enableSeconds = seconds;
        StopAllCoroutines();
        StartCoroutine(CoEnable());
    }

    IEnumerator CoEnable()
    {
        gameObject.SetActive(true);

        float t = 0f;
        while (t < enableSeconds)
        {
            t += Time.deltaTime;
            var cols = Physics.OverlapBox(_col.bounds.center, _col.bounds.extents, transform.rotation, zombieLayer);
            foreach (var c in cols)
            {
                var rb = c.attachedRigidbody;
                if (rb)
                {
                    Vector3 dir = (c.transform.position - _col.bounds.ClosestPoint(c.transform.position)).normalized;
                    rb.AddForce(dir * pushOutForce, ForceMode.VelocityChange);
                }
            }
            yield return null;
        }

        gameObject.SetActive(false);
    }
}
