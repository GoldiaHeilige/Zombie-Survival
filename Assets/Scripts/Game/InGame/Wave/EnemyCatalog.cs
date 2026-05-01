using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemy Catalog", fileName = "EnemyCatalog")]
public class EnemyCatalog : ScriptableObject
{
    public List<EnemyDefinition> entries = new List<EnemyDefinition>();
}
