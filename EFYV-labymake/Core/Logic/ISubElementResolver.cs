using EFYVLabyMake.Core.Models;

namespace EFYVLabyMake.Core.Logic
{
    // Item #6: resolves an attachment's sub-element NAME to its pixel data at
    // export time so the export engine can flatten attachments into the atlas.
    // AssetBankManager is the production implementation; the export engine
    // treats a null resolver (or an unresolved name) as "cannot flatten" and
    // still emits the structured attachment metadata (documented behavior).
    public interface ISubElementResolver
    {
        // Returns false (element null) when the name cannot be resolved -
        // missing file, unreadable file, or a resolver without a backing
        // store. Implementations must not throw for unresolvable names.
        bool TryResolveSubElement(string name, out SubElement element);
    }
}
