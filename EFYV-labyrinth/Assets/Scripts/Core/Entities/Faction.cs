namespace EFYV.Core.Entities
{
    // Which side of combat an attack belongs to. Weapons and projectiles carry the
    // faction of their owner so targeting and damage never hit the owner's own side.
    // Player is deliberately the zero value: unowned weapons and projectiles created
    // outside a WeaponController default to the player's side, matching the historic
    // behavior before factions existed.
    public enum Faction
    {
        Player = 0,
        Enemy = 1
    }
}
