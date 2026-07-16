using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using EFYV.Core.Data;
using EFYV.Core.Data.Entities;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Models;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;
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
            RegisterAssetFactory<UnityEntityData>(nameof(UnityEntityData));
            RegisterAssetFactory<GameAssetData>(nameof(GameAssetData));
            RegisterAssetFactory<LivingEntityData>(GameConfig.Importer.AssetTypeLivingEntityData);
            RegisterAssetFactory<EnemyData>(GameConfig.Importer.AssetTypeEnemyData);
            RegisterAssetFactory<BossData>(GameConfig.Importer.AssetTypeBossData);

            RegisterAssetFactory<EvilEyeData>(nameof(EvilEyeData));
            RegisterAssetFactory<EyeBearerData>(nameof(EyeBearerData));
            RegisterAssetFactory<SphinxKittenData>(nameof(SphinxKittenData));
            RegisterAssetFactory<SphinxCatData>(nameof(SphinxCatData));
            RegisterAssetFactory<BabyMummiesData>(nameof(BabyMummiesData));
            RegisterAssetFactory<FemaleMummyData>(nameof(FemaleMummyData));
            RegisterAssetFactory<MaleMummyData>(nameof(MaleMummyData));
            RegisterAssetFactory<TutData>(nameof(TutData));
            RegisterAssetFactory<AnkhesenpaatenData>(nameof(AnkhesenpaatenData));
            RegisterAssetFactory<EyeOfProvidenceFakeData>(nameof(EyeOfProvidenceFakeData));
            RegisterAssetFactory<EyeOfProvidenceRealData>(nameof(EyeOfProvidenceRealData));
            RegisterAssetFactory<PharaohAkhenatenData>(nameof(PharaohAkhenatenData));
            RegisterAssetFactory<NefertitiData>(nameof(NefertitiData));
            RegisterAssetFactory<PyramidsData>(nameof(PyramidsData));
            RegisterAssetFactory<CactusData>(nameof(CactusData));
            RegisterAssetFactory<PyramidDoorData>(nameof(PyramidDoorData));
            RegisterAssetFactory<ClosedSarcophageData>(nameof(ClosedSarcophageData));
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

            var slices = new SpriteMetaData[frameCount];
            string spriteName = Path.GetFileNameWithoutExtension(importer.assetPath);
            for (int i = GameConfig.Runtime.FirstIndex; i < frameCount; i++)
            {
                int column = i % columns;
                int row = i / columns;
                slices[i] = new SpriteMetaData
                {
                    name = spriteName + GameConfig.Importer.SpriteSliceNameSeparator + i.ToString(GameConfig.Importer.SpriteSliceIndexFormat),
                    rect = new Rect(
                        column * metadata.frameWidth,
                        metadata.atlasHeight - ((row + GameConfig.Runtime.ExclusiveUpperBoundOffset) * metadata.frameHeight),
                        metadata.frameWidth,
                        metadata.frameHeight),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(GameConfig.Importer.SpritePivotNormalized, GameConfig.Importer.SpritePivotNormalized)
                };
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritesheet = slices;
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

            foreach (string metadataPath in metadataPaths)
            {
                try
                {
                    ImportEFYVAsset(metadataPath);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static void ImportEFYVAsset(string path)
        {
            Debug.Log(string.Format(GameConfig.Importer.LogDetected, path));
            
            // 1. MIGRATION: Read the JSON using the Backend Ultra-Fast Parser
            EFYVJsonFormat data = FastImporter.ParseEfyvFile(path);

            if (data.properties == null)
            {
                Debug.LogError(GameConfig.Importer.LogError);
                return;
            }

            // Extract entity name from properties
            string extractedEntityName = GameConfig.Importer.DefaultEntityName;
            if (data.properties != null && data.properties.ContainsKey(GameConfig.Importer.KeyEntityName))
            {
                extractedEntityName = data.properties[GameConfig.Importer.KeyEntityName].GetString();
            }
            else if (data.properties != null && data.properties.ContainsKey(EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.AssetNameField))
            {
                extractedEntityName = data.properties[EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.AssetNameField].GetString();
            }
            if (!SafePathPolicy.IsSafeFileStem(extractedEntityName))
            {
                Debug.LogError(GameConfig.Importer.LogError);
                return;
            }
            if (data.atlas.HasValue && !IsValidAtlasMetadata(data.atlas.Value))
            {
                Debug.LogError(GameConfig.Importer.LogError);
                return;
            }

            if (string.IsNullOrEmpty(data.assetType) || !AssetTypes.TryGetValue(data.assetType, out Type expectedAssetType))
            {
                Debug.LogError(GameConfig.Importer.LogError);
                return;
            }

            // 2. Define path for the ScriptableObject
            string directory = Path.GetDirectoryName(path);
            string assetPath = directory + GameConfig.Importer.PathSeparator + extractedEntityName + GameConfig.Importer.ExtensionAsset;

            // 3. Create or Update the ScriptableObject
            SchemaBackedAssetData assetData = AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(assetPath);
            bool isNew = GameConfig.Importer.InitialIsNewAsset;
            if (assetData == null)
            {
                assetData = CreateAssetData(data.assetType);
                isNew = GameConfig.Importer.IsNewAsset;
            }
            else if (!expectedAssetType.IsInstanceOfType(assetData))
            {
                Debug.LogError(GameConfig.Importer.LogError);
                return;
            }

            // 4. Map the data from the JSON into the OOP ScriptableObject
            if (data.properties != null)
            {
                if (assetData is UnityEntityData entityData)
                {
                    entityData.entityName = extractedEntityName;
                }
                else if (assetData is GameAssetData gameAssetData)
                {
                    gameAssetData.assetName = extractedEntityName;
                }
                 
                var block = assetData.GetSchemaBlock();
                ApplySchemaProperties(data.properties, ref block);
                assetData.SetSchemaBlock(block);

            }

            // 5. Try to link the PNG if it exists next to the JSON
            string pngPath = GetSiblingTexturePath(path);
            EnsureTextureImportIsCurrent(pngPath, data.atlas);
            Sprite[] importedSprites = LoadSprites(pngPath);
            Sprite loadedSprite = importedSprites.Length > GameConfig.Runtime.EmptyCollectionCount
                ? importedSprites[GameConfig.Runtime.FirstIndex]
                : null;
            EntityAtlasMetadata importedAtlas = ConvertAtlasMetadata(data.atlas);
            EntityHitboxRecord[] importedHitboxes = ConvertHitboxes(data.hitboxes);
            bool hasFacing = TryGetFacing(data, out FacingDirection facing);
            if (assetData is LivingEntityData livingData && hasFacing)
            {
                livingData.SetImportedFacing(facing, importedAtlas, importedSprites, importedHitboxes);
            }
            else if (assetData is UnityEntityData entityData)
            {
                entityData.SetImportedAtlas(importedAtlas, importedSprites);
                entityData.SetImportedHitboxes(importedHitboxes);
            }
            else if (loadedSprite != null && assetData is GameAssetData gameAssetData)
            {
                gameAssetData.sprite = loadedSprite;
            }

            // 6. Save changes to Unity
            if (isNew)
            {
                AssetDatabase.CreateAsset(assetData, assetPath);
            }
             
            EditorUtility.SetDirty(assetData);
            AssetDatabase.SaveAssets();
            EFYVLiveDebugBridge.QueueRefresh(assetData);
            
            Debug.Log(string.Format(GameConfig.Importer.LogSuccess, extractedEntityName));
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
            if (properties.TryGetValue(GameConfig.Importer.KeyMaxHealth, out System.Text.Json.JsonElement maxHealth))
                block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.MaxHealth, maxHealth.GetSingle());
            if (properties.TryGetValue(GameConfig.Importer.KeyBaseSpeed, out System.Text.Json.JsonElement baseSpeed))
                block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.BaseSpeed, baseSpeed.GetSingle());
            if (properties.TryGetValue(GameConfig.Importer.KeyDamageToPlayer, out System.Text.Json.JsonElement damageToPlayer))
                block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.DamageToPlayer, damageToPlayer.GetSingle());
            if (properties.TryGetValue(GameConfig.Importer.KeyExperienceValue, out System.Text.Json.JsonElement experienceValue))
                block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.ExperienceValue, experienceValue.GetSingle());
            if (properties.TryGetValue(GameConfig.Importer.KeyPhase2HealthThreshold, out System.Text.Json.JsonElement phase2HealthThreshold))
                block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.Phase2HealthThreshold, phase2HealthThreshold.GetSingle());
        }

        internal static bool IsValidAtlasMetadata(AtlasMetadataJson metadata)
        {
            if (metadata.formatVersion != BackendConfig.Exporter.CurrentFormatVersion ||
                metadata.frameWidth <= GameConfig.Runtime.EmptyCollectionCount ||
                metadata.frameHeight <= GameConfig.Runtime.EmptyCollectionCount ||
                metadata.atlasWidth <= GameConfig.Runtime.EmptyCollectionCount ||
                metadata.atlasHeight <= GameConfig.Runtime.EmptyCollectionCount ||
                metadata.atlasWidth > GameConfig.Importer.MaxTextureSize ||
                metadata.atlasHeight > GameConfig.Importer.MaxTextureSize ||
                (long)metadata.atlasWidth * metadata.atlasHeight > GameConfig.Importer.MaxAtlasPixelCount ||
                metadata.atlasWidth % metadata.frameWidth != GameConfig.Runtime.EmptyCollectionCount ||
                metadata.atlasHeight % metadata.frameHeight != GameConfig.Runtime.EmptyCollectionCount ||
                metadata.animations == null)
                return false;

            long capacity = ((long)metadata.atlasWidth / metadata.frameWidth) *
                ((long)metadata.atlasHeight / metadata.frameHeight);
            if (capacity <= GameConfig.Runtime.EmptyCollectionCount || capacity > int.MaxValue) return false;

            for (int i = GameConfig.Runtime.FirstIndex; i < metadata.animations.Count; i++)
            {
                AnimationMetadataJson animation = metadata.animations[i];
                if (string.IsNullOrWhiteSpace(animation.name) ||
                    animation.fps <= GameConfig.Runtime.EmptyCollectionCount ||
                    animation.startFrame < GameConfig.Runtime.FirstIndex ||
                    animation.frameCount <= GameConfig.Runtime.EmptyCollectionCount ||
                    (long)animation.startFrame + animation.frameCount > capacity)
                    return false;
            }
            return true;
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

            SpriteMetaData[] slices = importer.spritesheet;
            if (slices == null || slices.Length != frameCount) return true;
            for (int i = GameConfig.Runtime.FirstIndex; i < slices.Length; i++)
            {
                if (!Mathf.Approximately(slices[i].rect.width, metadata.frameWidth) ||
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
                animations[i] = new EntityAnimationMetadata
                {
                    Name = animation.name,
                    FramesPerSecond = animation.fps,
                    StartFrame = animation.startFrame,
                    FrameCount = animation.frameCount
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
