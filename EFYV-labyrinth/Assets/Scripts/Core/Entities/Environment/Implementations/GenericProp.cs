namespace EFYV.Core.Entities.Environment.Implementations
{
    // Item #4: the neutral prop archetype the debug spawn factory instantiates
    // for any imported GameAssetData (plain props, tilesets, and custom prop
    // types). A bare NonInteractableProp - it shows the imported sprite and
    // animation frames and takes its blocking from the asset's IsWalkable schema
    // slot (bound through PropEntity.LoadData), without the theme-specific
    // behavior or forced walkability of the authored props.
    public class GenericProp : NonInteractableProp
    {
    }
}
