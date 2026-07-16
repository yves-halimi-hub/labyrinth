using UnityEngine;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Managers
{
    [RequireComponent(typeof(Camera))]
    public class MapTransitionCameraEffect : MonoBehaviour
    {
        private EFYVBackend.Core.Models.SystemData Data = new EFYVBackend.Core.Models.SystemData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        [Header(GameConfig.Map.HeaderBackendBlur)]
        [SerializeField] private bool enableCpuBlurFallback = GameConfig.Map.BlurEnabled;

        public int CurrentBlurRadius
        {
            get => Data.CurrentBlurRadius;
            set => Data.CurrentBlurRadius = value;
        }

        private void Awake()
        {
            Data.CurrentBlurRadius = GameConfig.Map.MinimumBlurRadius;
        }
        
        private Texture2D _screenTex;
        private Texture2D _blurTex;
        private Texture2D _scratchTex;
        
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!enableCpuBlurFallback || CurrentBlurRadius <= GameConfig.Map.MinimumBlurRadius)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // Allocate textures matching the screen resolution if needed
            if (_screenTex == null || _screenTex.width != source.width || _screenTex.height != source.height)
            {
                ReleaseTextures();
                _screenTex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, GameConfig.Map.TextureMipmapsEnabled);
                _blurTex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, GameConfig.Map.TextureMipmapsEnabled);
                _scratchTex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, GameConfig.Map.TextureMipmapsEnabled);
            }

            // Read the current camera output from the GPU back to the CPU
            RenderTexture.active = source;
            _screenTex.ReadPixels(new Rect(GameConfig.Map.TexturePixelOrigin, GameConfig.Map.TexturePixelOrigin, source.width, source.height), GameConfig.Map.TexturePixelOrigin, GameConfig.Map.TexturePixelOrigin);
            _screenTex.Apply(GameConfig.Map.TextureMipmapsEnabled);

            // Access the raw memory via Unity's NativeArray
            var srcData = _screenTex.GetRawTextureData<uint>();
            var destData = _blurTex.GetRawTextureData<uint>();
            var scratchData = _scratchTex.GetRawTextureData<uint>();

            unsafe
            {
                // Pin the NativeArrays into raw pointers to feed directly to our C-optimized backend
                uint* pSrc = (uint*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(srcData);
                uint* pDest = (uint*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(destData);
                uint* pScratch = (uint*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(scratchData);

                // Execute the heavily optimized backend sliding-window Box Blur
                EFYVBackend.Core.Memory.FastEffects.BoxBlur(
                    pSrc, 
                    pDest, 
                    pScratch,
                    source.width, 
                    source.height, 
                    CurrentBlurRadius
                );
            }

            _blurTex.Apply(GameConfig.Map.TextureMipmapsEnabled);

            // Blit the blurred CPU texture to the screen
            Graphics.Blit(_blurTex, destination);
            RenderTexture.active = null;
        }

        private void OnDestroy()
        {
            ReleaseTextures();
        }

        private void ReleaseTextures()
        {
            if (_screenTex != null) Destroy(_screenTex);
            if (_blurTex != null) Destroy(_blurTex);
            if (_scratchTex != null) Destroy(_scratchTex);
            _screenTex = null;
            _blurTex = null;
            _scratchTex = null;
        }
    }
}
