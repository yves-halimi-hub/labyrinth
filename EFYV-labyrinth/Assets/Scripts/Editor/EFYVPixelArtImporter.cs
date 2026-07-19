using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using System.IO;
using EFYV.Core.Data;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;
using LabyMakeConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using FacingDirection = EFYVBackend.Core.Math.FastMath.FacingDirection;
using UnityEntityData = EFYV.Core.Data.EntityData;

namespace EFYV.Editor
{
    public class EFYVPixelArtImporter : AssetPostprocessor
    {
        private static readonly Dictionary<string, Func<SchemaBackedAssetData>> AssetFactories =
            new Dictionary<string, Func<SchemaBackedAssetData>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> AssetTypes =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        static EFYVPixelArtImporter()
        {
            // Base archetypes: explicit, compile-checked registrations.
            RegisterAssetFactory<GameAssetData>(SharedConfig.GameAssetAssetType);
            RegisterAssetFactory<LivingEntityData>(GameConfig.Importer.AssetTypeLivingEntityData);
            RegisterAssetFactory<EnemyData>(GameConfig.Importer.AssetTypeEnemyData);
            RegisterAssetFactory<BossData>(GameConfig.Importer.AssetTypeBossData);

            // Specific designable types (#16e): generated from the shared
            // BuiltInAssetRegistrations table instead of a second hand-kept list.
            // A registration without a concrete C# class falls back to its base
            // archetype's factory, so config-only additions still import.
            Dictionary<string, Type> designableTypes = BuildDesignableTypeIndex();
            foreach (LabyMakeConfig.Schema.AssetRegistration registration in
                LabyMakeConfig.Schema.BuiltInAssetRegistrations)
            {
                RegisterGeneratedFactory(designableTypes, registration.AssetType, registration.BaseAssetType);
            }
        }

        private static Dictionary<string, Type> BuildDesignableTypeIndex()
        {
            var index = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in typeof(SchemaBackedAssetData).Assembly.GetTypes())
            {
                if (!type.IsAbstract && typeof(SchemaBackedAssetData).IsAssignableFrom(type))
                    index[type.Name] = type;
            }
            return index;
        }

        private static void RegisterGeneratedFactory(
            Dictionary<string, Type> designableTypes,
            string assetType,
            string baseAssetType)
        {
            if (designableTypes.TryGetValue(assetType, out Type resolved))
            {
                AssetFactories[assetType] = () => (SchemaBackedAssetData)ScriptableObject.CreateInstance(resolved);
                AssetTypes[assetType] = resolved;
                return;
            }

            if (AssetTypes.TryGetValue(baseAssetType, out Type baseType))
            {
                AssetFactories[assetType] = AssetFactories[baseAssetType];
                AssetTypes[assetType] = baseType;
            }
        }

        public static void RegisterAssetFactory<T>(string assetType) where T : SchemaBackedAssetData
        {
            if (string.IsNullOrEmpty(assetType)) throw new ArgumentException(nameof(assetType));
            AssetFactories[assetType] = () => ScriptableObject.CreateInstance<T>();
            AssetTypes[assetType] = typeof(T);
        }

        private void OnPreprocessTexture()
        {
            try
            {
                if (!assetPath.EndsWith(GameConfig.Importer.ExtensionPNG, StringComparison.OrdinalIgnoreCase)) return;

                string metadataPath = GetSiblingMetadataPath(assetPath);
                if (!File.Exists(metadataPath)) return;

                EFYVJsonFormat data = FastImporter.ParseEfyvFile(metadataPath);
                if (data.atlas.HasValue && !IsValidAtlasMetadata(data.atlas.Value)) return;
                ConfigureTextureImporter((TextureImporter)assetImporter, data.atlas);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static void ConfigureTextureImporter(TextureImporter importer, AtlasMetadataJson? atlas)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = GameConfig.Map.TextureMipmapsEnabled;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = SharedConfig.PixelsPerUnit;
            importer.maxTextureSize = GameConfig.Importer.MaxTextureSize;
            importer.npotScale = TextureImporterNPOTScale.None;

            if (!atlas.HasValue)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                return;
            }

            AtlasMetadataJson metadata = atlas.Value;
            int columns = metadata.atlasWidth / metadata.frameWidth;
            int rows = metadata.atlasHeight / metadata.frameHeight;
            int frameCount = GetAuthoredFrameCount(metadata, columns * rows);
            if (frameCount <= GameConfig.Runtime.ExclusiveUpperBoundOffset)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                return;
            }

            // Unity 6.6 removed SpriteMetaData access through
            // TextureImporter.spritesheet; slices go through the 2D Sprite
            // package's ISpriteEditorDataProvider instead. Existing spriteIDs
            // are reused by slice name so scene/prefab sprite references
            // survive re-imports.
            ISpriteEditorDataProvider provider = OpenSpriteDataProvider(importer);
            SpriteRect[] existing = provider.GetSpriteRects();
            var slices = new SpriteRect[frameCount];
            string spriteName = Path.GetFileNameWithoutExtension(importer.assetPath);
            for (int i = GameConfig.Runtime.FirstIndex; i < frameCount; i++)
            {
                int column = i % columns;
                int row = i / columns;
                string sliceName = spriteName + GameConfig.Importer.SpriteSliceNameSeparator + i.ToString(GameConfig.Importer.SpriteSliceIndexFormat);
                slices[i] = new SpriteRect
                {
                    name = sliceName,
                    rect = new Rect(
                        column * metadata.frameWidth,
                        metadata.atlasHeight - ((row + GameConfig.Runtime.ExclusiveUpperBoundOffset) * metadata.frameHeight),
                        metadata.frameWidth,
                        metadata.frameHeight),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(GameConfig.Importer.SpritePivotNormalized, GameConfig.Importer.SpritePivotNormalized),
                    spriteID = FindExistingSpriteId(existing, sliceName)
                };
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
            provider.SetSpriteRects(slices);
            SyncSpriteNameFileIds(provider, slices);
            provider.Apply();
        }

        private static ISpriteEditorDataProvider OpenSpriteDataProvider(TextureImporter importer)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            return provider;
        }

        private static GUID FindExistingSpriteId(SpriteRect[] existing, string sliceName)
        {
            if (existing != null)
            {
                for (int i = GameConfig.Runtime.FirstIndex; i < existing.Length; i++)
                {
                    if (existing[i] != null && string.Equals(existing[i].name, sliceName, StringComparison.Ordinal))
                        return existing[i].spriteID;
                }
            }
            return GUID.Generate();
        }

        private static void SyncSpriteNameFileIds(ISpriteEditorDataProvider provider, SpriteRect[] slices)
        {
            // Keeps the name<->fileId table in step with the rects so Unity can
            // bind Sprite sub-assets deterministically (provider contract since
            // Unity 2021.2; the stub provider returns null and skips this).
            ISpriteNameFileIdDataProvider nameFileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIds == null) return;
            var pairs = new List<SpriteNameFileIdPair>(slices.Length);
            for (int i = GameConfig.Runtime.FirstIndex; i < slices.Length; i++)
            {
                pairs.Add(new SpriteNameFileIdPair(slices[i].name, slices[i].spriteID));
            }
            nameFileIds.SetNameFileIdPairs(pairs);
        }

        private static int GetAuthoredFrameCount(AtlasMetadataJson metadata, int frameCapacity)
        {
            if (metadata.animations == null || metadata.animations.Count == GameConfig.Runtime.EmptyCollectionCount)
                return frameCapacity;

            int frameCount = GameConfig.Runtime.EmptyCollectionCount;
            for (int i = GameConfig.Runtime.FirstIndex; i < metadata.animations.Count; i++)
            {
                AnimationMetadataJson animation = metadata.animations[i];
                frameCount = Math.Max(frameCount, animation.startFrame + animation.frameCount);
            }
            return Math.Min(frameCount, frameCapacity);
        }

        // This is called automatically by Unity whenever an asset is imported or modified in the Assets folder
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var metadataPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string importedPath in importedAssets)
            {
                if (importedPath.EndsWith(GameConfig.Importer.ExtensionEFYV, StringComparison.OrdinalIgnoreCase))
                {
                    metadataPaths.Add(importedPath);
                }
                else if (importedPath.EndsWith(GameConfig.Importer.ExtensionPNG, StringComparison.OrdinalIgnoreCase))
                {
                    string metadataPath = GetSiblingMetadataPath(importedPath);
                    if (File.Exists(metadataPath)) metadataPaths.Add(metadataPath);
                }
            }

            // Item #27: coalesce the per-asset AssetDatabase.SaveAssets() into
            // ONE call for the whole postprocess group. Each ImportEFYVAsset now
            // only SetDirty's its asset; saving them all once here avoids a full
            // serialization pass per file when a batch (or a directional export's
            // four facings) lands together.
            bool anyDirtied = false;
            foreach (string metadataPath in metadataPaths)
            {
                try
                {
                    anyDirtied |= ImportEFYVAsset(metadataPath);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            if (anyDirtied) AssetDatabase.SaveAssets();
        }

        // Returns whether an asset was created or marked dirty (so the caller
        // can decide to flush once for the whole group).
        private static bool ImportEFYVAsset(string path)
        {
            Debug.Log(string.Format(GameConfig.Importer.LogDetected, path));

            // 1. MIGRATION: Read the JSON using the Backend Ultra-Fast Parser.
            // TryParse distinguishes a vanished file from a malformed one (#16c),
            // and every rejection below names its actual cause (#16d).
            EfyvParseResult parseResult = FastImporter.TryParse(path, out EFYVJsonFormat data);
            if (parseResult == EfyvParseResult.Missing)
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorMissingFile, path));
                return false;
            }
            if (parseResult == EfyvParseResult.Malformed)
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorMalformed, path));
                return false;
            }

            // Version-absent legacy files read as LegacyDocumentVersion (#16a).
            // The importer accepts the whole supported RANGE (item #10): every
            // documented version so far is backward-compatible, so only files
            // from the future (or below the floor) are rejected.
            if (data.EffectiveDocumentVersion < BackendConfig.Exporter.MinSupportedDocumentVersion ||
                data.EffectiveDocumentVersion > BackendConfig.Exporter.CurrentDocumentVersion)
            {
                Debug.LogError(string.Format(
                    GameConfig.Importer.LogErrorUnsupportedDocumentVersion,
                    data.EffectiveDocumentVersion,
                    path,
                    BackendConfig.Exporter.CurrentDocumentVersion));
                return false;
            }

            if (data.properties == null)
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorMissingProperties, path));
                return false;
            }

            // Identity is REQUIRED (#36): files without entityName/assetName are
            // rejected instead of collapsing onto a shared "UnknownEntity" asset.
            string extractedEntityName;
            if (data.properties.ContainsKey(GameConfig.Importer.KeyEntityName))
            {
                extractedEntityName = data.properties[GameConfig.Importer.KeyEntityName].GetString();
            }
            else if (data.properties.ContainsKey(SharedConfig.AssetNameField))
            {
                extractedEntityName = data.properties[SharedConfig.AssetNameField].GetString();
            }
            else
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorMissingIdentity, path));
                return false;
            }
            if (!SafePathPolicy.IsSafeFileStem(extractedEntityName))
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorUnsafeIdentity, path, extractedEntityName));
                return false;
            }
            if (data.atlas.HasValue &&
                !FastExporter.TryValidateAtlasMetadata(data.atlas.Value, out AtlasMetadataError atlasError))
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorInvalidAtlas, path, atlasError));
                return false;
            }
            // Item #6: attachment records validate through the shared backend
            // gate (same single-source rule as the atlas metadata).
            if (!FastExporter.TryValidateAttachments(data.attachments, out int invalidAttachmentIndex))
            {
                Debug.LogError(string.Format(
                    GameConfig.Importer.LogErrorInvalidAttachments,
                    path,
                    invalidAttachmentIndex));
                return false;
            }
            // Item #5: the tileset manifest validates through the shared
            // backend gate (against the sibling atlas block, whose frames
            // must be exactly tileSize-square). A document carrying one is a
            // tile-sheet export and imports as a TilesetAssetData.
            bool isTileset = data.tileset.HasValue;
            if (isTileset &&
                !FastExporter.TryValidateTilesetManifest(
                    data.tileset.Value,
                    data.atlas,
                    out TilesetManifestError tilesetError))
            {
                Debug.LogError(string.Format(
                    GameConfig.Importer.LogErrorInvalidTileset,
                    path,
                    tilesetError));
                return false;
            }

            // Factory resolution with base-type fallback (#16e): a custom
            // assetType the game has no class for imports as its baseAssetType.
            string factoryType = null;
            Type expectedAssetType = null;
            if (!string.IsNullOrEmpty(data.assetType) && AssetTypes.TryGetValue(data.assetType, out expectedAssetType))
            {
                factoryType = data.assetType;
            }
            else if (!string.IsNullOrEmpty(data.baseAssetType) &&
                AssetTypes.TryGetValue(data.baseAssetType, out expectedAssetType))
            {
                factoryType = data.baseAssetType;
            }
            else
            {
                Debug.LogError(string.Format(GameConfig.Importer.LogErrorUnknownAssetType, path, data.assetType));
                return false;
            }

            // Item #5: a tileset document materializes as the dedicated
            // TilesetAssetData (a GameAssetData carrying the sliced tile
            // sprites in tile-id order), regardless of which GameAssetData-
            // family factory its assetType names.
            if (isTileset) expectedAssetType = typeof(TilesetAssetData);

            // 2. Define path for the ScriptableObject
            string directory = Path.GetDirectoryName(path);
            string assetPath = directory + GameConfig.Importer.PathSeparator + extractedEntityName + GameConfig.Importer.ExtensionAsset;

            // 3. Create or Update the ScriptableObject
            SchemaBackedAssetData assetData = AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(assetPath);
            bool isNew = GameConfig.Importer.InitialIsNewAsset;
            if (assetData == null)
            {
                assetData = isTileset
                    ? ScriptableObject.CreateInstance<TilesetAssetData>()
                    : CreateAssetData(factoryType);
                isNew = GameConfig.Importer.IsNewAsset;
            }
            else if (!expectedAssetType.IsInstanceOfType(assetData))
            {
                Debug.LogError(string.Format(
                    GameConfig.Importer.LogErrorExistingAssetTypeMismatch,
                    path,
                    assetData.GetType().Name,
                    expectedAssetType.Name));
                return false;
            }

            // 4. Map the data from the JSON into the OOP ScriptableObject
            if (assetData is UnityEntityData entityData)
            {
                entityData.entityName = extractedEntityName;
            }
            else if (assetData is GameAssetData gameAssetData)
            {
                gameAssetData.assetName = extractedEntityName;
            }

            var block = assetData.GetSchemaBlock();
            var unknownKeys = new List<string>();
            ApplySchemaProperties(data.properties, ref block, unknownKeys);
            assetData.SetSchemaBlock(block);
            if (unknownKeys.Count > GameConfig.Runtime.EmptyCollectionCount)
            {
                // Unknown keys are LOGGED, never silently dropped (#15): a typo'd
                // or not-yet-mapped designer field must be visible in the console.
                Debug.LogWarning(string.Format(
                    GameConfig.Importer.LogWarningUnknownSchemaKeys,
                    path,
                    string.Join(", ", unknownKeys)));
            }
            // Item #33b: unknown keys additionally land on the asset's
            // string-keyed custom-property store so custom-registered
            // consumers can read runtime-registered designer fields; an
            // import without unknown keys CLEARS stale entries.
            StoreCustomProperties(assetData, data.properties, unknownKeys);

            // 5. Try to link the PNG if it exists next to the JSON
            string pngPath = GetSiblingTexturePath(path);
            EnsureTextureImportIsCurrent(pngPath, data.atlas);
            Sprite[] importedSprites = LoadSprites(pngPath);
            Sprite loadedSprite = importedSprites.Length > GameConfig.Runtime.EmptyCollectionCount
                ? importedSprites[GameConfig.Runtime.FirstIndex]
                : null;
            EntityAtlasMetadata importedAtlas = ConvertAtlasMetadata(data.atlas);
            EntityHitboxRecord[] importedHitboxes = ConvertHitboxes(data.hitboxes);
            // Item #6: attachment records land on the schema-backed base for
            // EVERY asset shape (facing-directional entities store them at the
            // base level too - per-facing granularity is deferred alongside
            // dynamic rendering).
            assetData.SetImportedAttachments(ConvertAttachments(data.attachments));
            // Item #5: populate the tileset payload - manifest names/size plus
            // the sliced sprites in tile-id order (LoadSprites sorts by the
            // zero-padded slice name, which IS atlas frame order, and the
            // atlas block declares exactly tileCount frames).
            if (isTileset && assetData is TilesetAssetData tilesetAssetData)
            {
                TilesetManifestJson manifest = data.tileset.Value;
                tilesetAssetData.tileSize = manifest.tileSize;
                tilesetAssetData.tileNames = manifest.tiles.ToArray();
                var tileSprites = new Sprite[manifest.tiles.Count];
                int available = Math.Min(tileSprites.Length, importedSprites.Length);
                for (int i = GameConfig.Runtime.FirstIndex; i < available; i++)
                    tileSprites[i] = importedSprites[i];
                tilesetAssetData.tileSprites = tileSprites;
            }

            bool hasFacing = TryGetFacing(data, out FacingDirection facing);
            if (assetData is LivingEntityData livingData && hasFacing)
            {
                livingData.SetImportedFacing(facing, importedAtlas, importedSprites, importedHitboxes);
            }
            else if (assetData is UnityEntityData plainEntityData)
            {
                plainEntityData.SetImportedAtlas(importedAtlas, importedSprites);
                plainEntityData.SetImportedHitboxes(importedHitboxes);
            }
            else if (loadedSprite != null && assetData is GameAssetData spriteAssetData)
            {
                // Item #13: store the full imported frame set (sets sprite to
                // frame 0) so animated props can play the designer's frames.
                spriteAssetData.SetImportedFrames(importedSprites);
            }

            // 6. Save changes to Unity
            if (isNew)
            {
                AssetDatabase.CreateAsset(assetData, assetPath);
            }

            // Item #27: SetDirty here; the SaveAssets flush is coalesced to one
            // call per postprocess group by OnPostprocessAllAssets.
            EditorUtility.SetDirty(assetData);
            EFYVLiveDebugBridge.QueueRefresh(assetData);

            Debug.Log(string.Format(GameConfig.Importer.LogSuccess, extractedEntityName));
            return true;
        }

        private static SchemaBackedAssetData CreateAssetData(string assetType)
        {
            if (!string.IsNullOrEmpty(assetType) && AssetFactories.TryGetValue(assetType, out Func<SchemaBackedAssetData> factory))
                return factory();

            return null;
        }

        internal static string GetSiblingTexturePath(string metadataPath)
        {
            return Path.ChangeExtension(metadataPath, GameConfig.Importer.ExtensionPNG);
        }

        internal static string GetSiblingMetadataPath(string texturePath)
        {
            return Path.ChangeExtension(texturePath, GameConfig.Importer.ExtensionEFYV);
        }

        internal static void ApplySchemaProperties(
            Dictionary<string, System.Text.Json.JsonElement> properties,
            ref EFYVBackend.Core.Data.FastSchemaBlock block)
        {
            ApplySchemaProperties(properties, ref block, null);
        }

        // Manifest-driven mapping (#15): every slot in the shared wire-format
        // table is applied; keys outside the manifest (and outside the known
        // identity/routing keys) are reported back for logging.
        internal static void ApplySchemaProperties(
            Dictionary<string, System.Text.Json.JsonElement> properties,
            ref EFYVBackend.Core.Data.FastSchemaBlock block,
            ICollection<string> unknownKeys)
        {
            foreach (SharedConfig.SchemaFieldMapping mapping in SharedConfig.AssetSchemaFieldManifest)
            {
                if (!properties.TryGetValue(mapping.FieldName, out System.Text.Json.JsonElement element)) continue;

                if (mapping.Kind == SharedConfig.SchemaFieldKind.Boolean)
                {
                    bool value;
                    if (element.ValueKind == System.Text.Json.JsonValueKind.True) value = true;
                    else if (element.ValueKind == System.Text.Json.JsonValueKind.False) value = false;
                    else value = element.GetInt32() != BackendConfig.Serialization.FalseValue;
                    block.SetInt(
                        mapping.Slot,
                        value ? BackendConfig.Serialization.TrueValue : BackendConfig.Serialization.FalseValue);
                }
                else
                {
                    block.SetFloat(mapping.Slot, element.GetSingle());
                }
            }

            if (unknownKeys == null) return;
            foreach (KeyValuePair<string, System.Text.Json.JsonElement> property in properties)
            {
                if (!IsKnownPropertyKey(property.Key)) unknownKeys.Add(property.Key);
            }
        }

        // Item #33b: parks every unknown properties key on the asset as a
        // string key/value pair. Values keep their raw JSON text (strings
        // unquoted via GetString) - the store is type-agnostic by design;
        // SchemaBackedAssetData's typed accessors parse on read.
        private static void StoreCustomProperties(
            SchemaBackedAssetData assetData,
            Dictionary<string, System.Text.Json.JsonElement> properties,
            List<string> unknownKeys)
        {
            if (unknownKeys.Count == GameConfig.Runtime.EmptyCollectionCount)
            {
                assetData.SetCustomProperties(null, null);
                return;
            }

            var keys = new string[unknownKeys.Count];
            var values = new string[unknownKeys.Count];
            for (int i = GameConfig.Runtime.FirstIndex; i < unknownKeys.Count; i++)
            {
                keys[i] = unknownKeys[i];
                System.Text.Json.JsonElement element = properties[unknownKeys[i]];
                values[i] = element.ValueKind == System.Text.Json.JsonValueKind.String
                    ? element.GetString()
                    : element.GetRawText();
            }
            assetData.SetCustomProperties(keys, values);
        }

        private static bool IsKnownPropertyKey(string key)
        {
            foreach (SharedConfig.SchemaFieldMapping mapping in SharedConfig.AssetSchemaFieldManifest)
            {
                if (string.Equals(mapping.FieldName, key, StringComparison.Ordinal)) return true;
            }
            foreach (string nonSchemaField in SharedConfig.NonSchemaPropertyFields)
            {
                if (string.Equals(nonSchemaField, key, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // Thin wrapper over the ONE shared validator in the backend (#16b).
        internal static bool IsValidAtlasMetadata(AtlasMetadataJson metadata)
        {
            return FastExporter.TryValidateAtlasMetadata(metadata, out _);
        }

        private static bool TryGetFacing(EFYVJsonFormat data, out FacingDirection facing)
        {
            facing = default;
            if (data.properties == null || !data.properties.TryGetValue(GameConfig.Importer.KeyFacing, out System.Text.Json.JsonElement value))
                return false;

            string authoredFacing = value.GetString();
            if (authoredFacing == GameConfig.Importer.FacingUp) facing = FacingDirection.Up;
            else if (authoredFacing == GameConfig.Importer.FacingDown) facing = FacingDirection.Down;
            else if (authoredFacing == GameConfig.Importer.FacingLeft) facing = FacingDirection.Left;
            else if (authoredFacing == GameConfig.Importer.FacingRight) facing = FacingDirection.Right;
            else return false;
            return true;
        }

        private static void EnsureTextureImportIsCurrent(string pngPath, AtlasMetadataJson? atlas)
        {
            if (!File.Exists(pngPath)) return;

            TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null || !RequiresTextureReimport(importer, atlas)) return;
            // Item #27: ForceSynchronousImport is REQUIRED here (not incidental):
            // the caller reads the freshly sliced sprites with LoadSprites right
            // after this returns, so the re-import must complete inline. It also
            // only fires when RequiresTextureReimport found the settings/slices
            // actually stale, so a live cycle that changed no pixels never
            // triggers it.
            AssetDatabase.ImportAsset(
                pngPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool RequiresTextureReimport(TextureImporter importer, AtlasMetadataJson? atlas)
        {
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.mipmapEnabled != GameConfig.Map.TextureMipmapsEnabled ||
                importer.filterMode != FilterMode.Point ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                !importer.alphaIsTransparency ||
                !Mathf.Approximately(importer.spritePixelsPerUnit, SharedConfig.PixelsPerUnit) ||
                importer.maxTextureSize != GameConfig.Importer.MaxTextureSize ||
                importer.npotScale != TextureImporterNPOTScale.None)
                return true;

            if (!atlas.HasValue) return importer.spriteImportMode != SpriteImportMode.Single;

            AtlasMetadataJson metadata = atlas.Value;
            int capacity = (metadata.atlasWidth / metadata.frameWidth) * (metadata.atlasHeight / metadata.frameHeight);
            int frameCount = GetAuthoredFrameCount(metadata, capacity);
            SpriteImportMode expectedMode = frameCount > GameConfig.Runtime.ExclusiveUpperBoundOffset
                ? SpriteImportMode.Multiple
                : SpriteImportMode.Single;
            if (importer.spriteImportMode != expectedMode) return true;
            if (expectedMode == SpriteImportMode.Single) return false;

            SpriteRect[] slices = OpenSpriteDataProvider(importer).GetSpriteRects();
            if (slices == null || slices.Length != frameCount) return true;
            for (int i = GameConfig.Runtime.FirstIndex; i < slices.Length; i++)
            {
                if (slices[i] == null ||
                    !Mathf.Approximately(slices[i].rect.width, metadata.frameWidth) ||
                    !Mathf.Approximately(slices[i].rect.height, metadata.frameHeight))
                    return true;
            }
            return false;
        }

        private static Sprite[] LoadSprites(string path)
        {
            UnityEngine.Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            var sprites = new List<Sprite>(importedAssets.Length);
            for (int i = GameConfig.Runtime.FirstIndex; i < importedAssets.Length; i++)
            {
                if (importedAssets[i] is Sprite sprite) sprites.Add(sprite);
            }

            sprites.Sort((left, right) => StringComparer.Ordinal.Compare(left.name, right.name));
            return sprites.ToArray();
        }

        private static EntityAtlasMetadata ConvertAtlasMetadata(AtlasMetadataJson? atlas)
        {
            if (!atlas.HasValue) return default;

            AtlasMetadataJson metadata = atlas.Value;
            int animationCount = metadata.animations != null
                ? metadata.animations.Count
                : GameConfig.Runtime.EmptyCollectionCount;
            var animations = new EntityAnimationMetadata[animationCount];
            for (int i = GameConfig.Runtime.FirstIndex; i < animationCount; i++)
            {
                AnimationMetadataJson animation = metadata.animations[i];
                // Item #10 optional timing/playback fields resolve to their
                // effective defaults here so runtime code never re-derives
                // them: full loop range, forward playback, fps-only timing.
                int lastFrame = animation.frameCount - GameConfig.Runtime.ExclusiveUpperBoundOffset;
                animations[i] = new EntityAnimationMetadata
                {
                    Name = animation.name,
                    FramesPerSecond = animation.fps,
                    StartFrame = animation.startFrame,
                    FrameCount = animation.frameCount,
                    FrameDurationsMs = animation.frameDurationsMs != null
                        ? animation.frameDurationsMs.ToArray()
                        : null,
                    LoopStartFrame = animation.loopStart ?? GameConfig.Runtime.FirstIndex,
                    LoopEndFrame = animation.loopEnd ?? lastFrame,
                    PingPong = animation.pingPong ?? false,
                    Effects = ConvertEffects(animation.effects)
                };
            }

            return new EntityAtlasMetadata
            {
                FormatVersion = metadata.formatVersion,
                FrameWidth = metadata.frameWidth,
                FrameHeight = metadata.frameHeight,
                AtlasWidth = metadata.atlasWidth,
                AtlasHeight = metadata.atlasHeight,
                Animations = animations
            };
        }

        // Item #7: effect descriptors resolve their optional numeric params to
        // the shared Default* config values here, so runtime code never
        // re-derives them (mirrors the timing-field resolution above).
        private static EntityEffectDescriptor[] ConvertEffects(List<EffectDescriptorJson> effects)
        {
            if (effects == null || effects.Count == GameConfig.Runtime.EmptyCollectionCount) return null;

            var converted = new EntityEffectDescriptor[effects.Count];
            for (int i = GameConfig.Runtime.FirstIndex; i < effects.Count; i++)
            {
                EffectDescriptorJson effect = effects[i];
                converted[i] = new EntityEffectDescriptor
                {
                    Name = effect.name,
                    EffectType = effect.effectType,
                    Trigger = effect.trigger,
                    ColorRgba = effect.colorRgba ?? BackendConfig.Exporter.DefaultEffectColorRgba,
                    DurationMs = effect.durationMs ?? BackendConfig.Exporter.DefaultEffectDurationMs,
                    Strength = effect.strength ?? BackendConfig.Exporter.DefaultEffectStrength
                };
            }
            return converted;
        }

        // Item #6: attachment records convert 1:1; absent optional flips
        // resolve to false here so runtime code never re-derives them
        // (mirrors the timing/effect-field resolution above). A document
        // without attachments stores null (no empty-array spam).
        private static EntityAttachmentRecord[] ConvertAttachments(List<AttachmentJson> attachments)
        {
            if (attachments == null || attachments.Count == GameConfig.Runtime.EmptyCollectionCount)
                return null;

            var records = new EntityAttachmentRecord[attachments.Count];
            for (int i = GameConfig.Runtime.FirstIndex; i < attachments.Count; i++)
            {
                AttachmentJson attachment = attachments[i];
                records[i] = new EntityAttachmentRecord
                {
                    FrameIndex = attachment.frameIndex,
                    SubElementName = attachment.subElement,
                    X = attachment.x,
                    Y = attachment.y,
                    ZOrder = attachment.zOrder,
                    FlipX = attachment.flipX ?? false,
                    FlipY = attachment.flipY ?? false
                };
            }
            return records;
        }

        private static EntityHitboxRecord[] ConvertHitboxes(List<HitboxJson> hitboxes)
        {
            int count = hitboxes != null ? hitboxes.Count : GameConfig.Runtime.EmptyCollectionCount;
            var records = new EntityHitboxRecord[count];
            for (int i = GameConfig.Runtime.FirstIndex; i < count; i++)
            {
                HitboxJson hitbox = hitboxes[i];
                records[i] = new EntityHitboxRecord
                {
                    FrameIndex = hitbox.frameIndex,
                    HitboxType = hitbox.hitboxType,
                    Bounds = new Rect(hitbox.x, hitbox.y, hitbox.width, hitbox.height)
                };
            }
            return records;
        }
    }
}
