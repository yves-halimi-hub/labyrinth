using System.Collections.Generic;
using EFYV.Core.Data;
using RuntimeConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.Runtime;

namespace EFYV.Core.Spawning
{
    // Item #4: the testable core of the debug spawn window - the list / refresh
    // / selection state machine. The editor window feeds it the assets it
    // discovered under Assets/RawArt (and the bridge's most-recently-refreshed
    // asset); this class owns dedup, selection preservation, and the auto-offer
    // of the newest import. It has no editor or Unity-GUI dependency, so it is
    // unit-tested headlessly and the window stays a thin shell around it.
    public sealed class SpawnPaletteModel
    {
        // One row in the palette: the imported asset, its display name, and
        // whether the factory can spawn it (an archetype resolved) plus which
        // archetype, so the window can label spawnable vs. inert assets.
        public readonly struct Entry
        {
            public SchemaBackedAssetData Asset { get; }
            public string DisplayName { get; }
            public bool CanSpawn { get; }
            public SpawnArchetype Archetype { get; }

            public Entry(SchemaBackedAssetData asset, string displayName, bool canSpawn, SpawnArchetype archetype)
            {
                Asset = asset;
                DisplayName = displayName;
                CanSpawn = canSpawn;
                Archetype = archetype;
            }
        }

        private readonly List<Entry> entries = new List<Entry>();
        private int selectedIndex = RuntimeConfig.NotFoundIndex;
        private SchemaBackedAssetData pendingAutoOffer;

        public IReadOnlyList<Entry> Entries => entries;
        public int Count => entries.Count;
        public int SelectedIndex => selectedIndex;

        public SchemaBackedAssetData SelectedAsset =>
            selectedIndex >= RuntimeConfig.FirstIndex && selectedIndex < entries.Count
                ? entries[selectedIndex].Asset
                : null;

        public bool HasSelection => SelectedAsset != null;

        public bool TryGetSelectedEntry(out Entry entry)
        {
            if (selectedIndex >= RuntimeConfig.FirstIndex && selectedIndex < entries.Count)
            {
                entry = entries[selectedIndex];
                return true;
            }
            entry = default;
            return false;
        }

        // Rebuilds the entry list from the freshly discovered assets: nulls are
        // skipped, duplicates (by reference) collapse, and the given order is
        // preserved. Selection survives when it can: a pending auto-offer that
        // has now appeared wins first (the newest import gets offered), otherwise
        // the previously-selected asset keeps its selection if still present,
        // otherwise selection falls back to the first entry (or none when empty).
        public void SetAssets(IReadOnlyList<SchemaBackedAssetData> assets)
        {
            SchemaBackedAssetData previouslySelected = SelectedAsset;
            entries.Clear();
            if (assets != null)
            {
                for (int i = RuntimeConfig.FirstIndex; i < assets.Count; i++)
                {
                    SchemaBackedAssetData asset = assets[i];
                    if (asset == null || IndexOfAsset(asset) >= RuntimeConfig.FirstIndex) continue;
                    entries.Add(BuildEntry(asset));
                }
            }
            selectedIndex = ResolveSelection(previouslySelected);
        }

        // Records the most-recently imported/refreshed asset (the seam the
        // importer feeds through EFYVLiveDebugBridge). If it is already listed it
        // is selected immediately; otherwise it is remembered and auto-selected
        // the next time SetAssets rediscovers it (a fresh import lands as an
        // asset before the palette has re-scanned the folder).
        public void NotifyRefreshed(SchemaBackedAssetData asset)
        {
            if (asset == null) return;
            int index = IndexOfAsset(asset);
            if (index >= RuntimeConfig.FirstIndex)
            {
                selectedIndex = index;
                pendingAutoOffer = null;
            }
            else
            {
                pendingAutoOffer = asset;
            }
        }

        public bool Select(int index)
        {
            if (index < RuntimeConfig.FirstIndex || index >= entries.Count) return false;
            selectedIndex = index;
            return true;
        }

        private int ResolveSelection(SchemaBackedAssetData previouslySelected)
        {
            if (pendingAutoOffer != null)
            {
                int pendingIndex = IndexOfAsset(pendingAutoOffer);
                if (pendingIndex >= RuntimeConfig.FirstIndex)
                {
                    pendingAutoOffer = null;
                    return pendingIndex;
                }
            }

            if (previouslySelected != null)
            {
                int previousIndex = IndexOfAsset(previouslySelected);
                if (previousIndex >= RuntimeConfig.FirstIndex) return previousIndex;
            }

            return entries.Count > RuntimeConfig.EmptyCollectionCount
                ? RuntimeConfig.FirstIndex
                : RuntimeConfig.NotFoundIndex;
        }

        private int IndexOfAsset(SchemaBackedAssetData asset)
        {
            for (int i = RuntimeConfig.FirstIndex; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i].Asset, asset)) return i;
            }
            return RuntimeConfig.NotFoundIndex;
        }

        private static Entry BuildEntry(SchemaBackedAssetData asset)
        {
            string displayName = DataToPrefabFactory.ResolveDisplayName(asset);
            bool canSpawn = DataToPrefabFactory.TryResolveArchetype(asset, out SpawnArchetype archetype);
            return new Entry(asset, displayName, canSpawn, archetype);
        }
    }
}
