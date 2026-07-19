namespace EFYV.Core.Spawning
{
    // Item #4: the three generic template archetypes the data-to-prefab factory
    // maps every imported SchemaBackedAssetData onto. Enemy and Boss are the
    // living-entity archetypes (bound through LivingEntity.LoadData); Prop is the
    // GameAssetData archetype (bound through PropEntity.LoadData). One template
    // prefab (and therefore one PoolManager pool) backs each archetype.
    public enum SpawnArchetype
    {
        Enemy,
        Boss,
        Prop
    }
}
