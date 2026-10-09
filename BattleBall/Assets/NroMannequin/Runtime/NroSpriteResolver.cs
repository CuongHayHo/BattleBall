// =========================================================================================
// File: Assets/NroMannequin/Runtime/NroSpriteResolver.cs
// Description: Multi-Resolution Sprite Resolver for NRO Mannequin & Costume Tester.
// Supports 2-branch resolution (Atlas Big0..Big4 vs Loose PNG icon/x{zoom}/) across x1..x4.
// =========================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NroRig
{
    /// <summary>
    /// Interface kết nối giữa Catalog và Sprite Resolver.
    /// </summary>
    public interface INroSpriteResolver
    {
        Sprite ResolveSprite(int iconId, int zoomLevel);
    }

    /// <summary>
    /// Bản ghi ánh xạ lát cắt Atlas từ tệp nhị phân update_data/image (2.997 bản ghi, 9 bytes/bản ghi).
    /// </summary>
    [Serializable]
    public struct ImageRecord
    {
        public byte bigImageId; // 0..4 (tương ứng Big0..Big4)
        public short x;         // pixel 1x (Top-Left)
        public short y;         // pixel 1x (Top-Left)
        public short w;         // pixel 1x width
        public short h;         // pixel 1x height

        /// <summary>
        /// Điều kiện hợp lệ để cắt từ Big Atlas:
        /// Tọa độ dương, kích thước hợp lệ, và không vượt ra ngoài biên 255x255 của Atlas.
        /// </summary>
        public bool IsAtlasCandidate =>
            bigImageId <= 4 &&
            x >= 0 && y >= 0 &&
            w > 0 && h > 0 &&
            (x + w) <= 255 &&
            (y + h) <= 255;
    }

    /// <summary>
    /// Trình phân giải Sprite Runtime hỗ trợ đa độ phân giải x1, x2, x3, x4 cho NRO Mannequin.
    /// Quản lý nạp nhị phân image, cache Texture2D Atlas, nạp Dynamic Icon rời, và chuẩn hóa PixelsPerUnit.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public class NroSpriteResolver : MonoBehaviour, INroSpriteResolver, IDisposable
    {
        private static NroSpriteResolver _instance;
        public static NroSpriteResolver Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<NroSpriteResolver>();
                    if (_instance == null)
                    {
                        var go = new GameObject("NroSpriteResolver");
                        _instance = go.AddComponent<NroSpriteResolver>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        // Đường dẫn mặc định tới thư mục dữ liệu nguồn (bản sao trong dự án: <project>/SourceData, chứa data/...)
        // Đường dẫn mặc định = <thư mục dự án>/SourceData (bản sao dữ liệu nguồn, chứa data/...)
        public static string GetDefaultServerRoot()
        {
            // In the Editor the project's SourceData copy is used when it exists; on a device (no SourceData) the assets downloaded from the server
            // (BattleBall.Net.BbAssetUpdater, same folder layout) are used.
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SourceData"));
            if (Directory.Exists(source)) return source;
            return BattleBall.Net.BbAssetCache.InstalledRoot ?? source;
        }

        [SerializeField] private string _serverRoot = "";
        public string ServerRootPath
        {
            get => string.IsNullOrEmpty(_serverRoot) ? GetDefaultServerRoot() : _serverRoot;
            set
            {
                if (_serverRoot != value)
                {
                    _serverRoot = value;
                    _isInitialized = false;
                }
            }
        }

        private int _currentZoomLevel = 2;
        public int CurrentZoomLevel => _currentZoomLevel;

        private bool _isInitialized;
        public bool IsInitialized => _isInitialized;

        // Mảng bản ghi nhị phân image (2.997 phần tử)
        private ImageRecord[] _imageRecords;
        public int RecordCount => _imageRecords != null ? _imageRecords.Length : 0;

        // Bộ nhớ đệm Texture Atlas: key = (bigImageId, zoomLevel)
        private readonly Dictionary<(int bigId, int zoom), Texture2D> _atlasCache =
            new Dictionary<(int bigId, int zoom), Texture2D>();

        // Bộ nhớ đệm Sprite hoàn chỉnh: key = (iconId, zoomLevel)
        private readonly Dictionary<(int iconId, int zoom), Sprite> _spriteCache =
            new Dictionary<(int iconId, int zoom), Sprite>(2048);

        // Sprite rỗng dự phòng tránh NullReferenceException
        private Sprite _dummyFallbackSprite;

        public int CachedSpriteCount => _spriteCache.Count;
        public int CachedAtlasCount => _atlasCache.Count;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                Initialize();
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        public void SetZoomLevel(int zoomLevel)
        {
            _currentZoomLevel = Mathf.Clamp(zoomLevel, 1, 4);
        }

        /// <summary>
        /// Khởi tạo và nạp file nhị phân image.
        /// </summary>
        public bool Initialize(string customServerRoot = null)
        {
            if (_isInitialized) return true;

            if (!string.IsNullOrEmpty(customServerRoot))
            {
                _serverRoot = customServerRoot;
            }

            string imagePath = ResolveImageBinaryPath();
            if (LoadImageBinary(imagePath))
            {
                _isInitialized = true;
                return true;
            }

            Debug.LogWarning($"[NroSpriteResolver] Không thể nạp image binary tại: {imagePath}. Cho phép chạy fallback loose icon.");
            _isInitialized = true; // Vẫn cho phép chạy ở chế độ fallback Loose Icon
            return false;
        }

        #region Binary Parsing (Feature 4)

        /// <summary>
        /// Tự động tìm đường dẫn tệp nhị phân update_data/image theo các vị trí ưu tiên.
        /// </summary>
        public string ResolveImageBinaryPath()
        {
            // 1. Unity Assets Data folder
            string p3 = Path.Combine(Application.dataPath, "NroMannequin", "Data", "image.bytes");
            if (File.Exists(p3)) return p3;

            // 2. Server source path
            string p1 = Path.Combine(ServerRootPath, "data", "update_data", "image");
            if (File.Exists(p1)) return p1;

            // 3. Unity StreamingAssets
            string p2 = Path.Combine(Application.streamingAssetsPath, "data", "update_data", "image");
            if (File.Exists(p2)) return p2;

            return p3;
        }

        /// <summary>
        /// Đọc và giải tuần tự hóa tệp nhị phân image (26.975 bytes).
        /// Cấu trúc: 2 bytes Big-Endian short (num = 2997), theo sau là num * 9 bytes.
        /// Mỗi bản ghi: bigImageId (1B), x (2B), y (2B), w (2B), h (2B).
        /// </summary>
        public bool LoadImageBinary(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length < 2) return false;

                int count = (data[0] << 8) | data[1];
                if (count <= 0) return false;

                _imageRecords = new ImageRecord[count];
                int offset = 2;
                int validAtlasCount = 0;

                for (int i = 0; i < count; i++)
                {
                    if (offset + 9 > data.Length) break;

                    byte bigId = data[offset++];
                    short x = (short)((data[offset++] << 8) | data[offset++]);
                    short y = (short)((data[offset++] << 8) | data[offset++]);
                    short w = (short)((data[offset++] << 8) | data[offset++]);
                    short h = (short)((data[offset++] << 8) | data[offset++]);

                    var record = new ImageRecord
                    {
                        bigImageId = bigId,
                        x = x,
                        y = y,
                        w = w,
                        h = h
                    };

                    _imageRecords[i] = record;
                    if (record.IsAtlasCandidate) validAtlasCount++;
                }

                Debug.Log($"[NroSpriteResolver] Đã nạp thành công {count} bản ghi image ({validAtlasCount} Atlas, {count - validAtlasCount} Fallback) từ: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NroSpriteResolver] Lỗi khi đọc image binary: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Atlas Texture Management (Feature 4 & 6)

        /// <summary>
        /// Lấy hoặc nạp Texture Atlas Big0..Big4 ở cấp độ zoom xác định.
        /// Tự động xử lý x1 bằng cách downsample từ x2 hoặc x4.
        /// </summary>
        public Texture2D GetAtlasTexture(int bigImageId, int zoomLevel)
        {
            if (bigImageId < 0 || bigImageId > 5) return null;
            zoomLevel = Mathf.Clamp(zoomLevel, 1, 4);

            var key = (bigImageId, zoomLevel);
            if (_atlasCache.TryGetValue(key, out Texture2D cachedTex) && cachedTex != null)
            {
                return cachedTex;
            }

            Texture2D loadedTex = LoadAtlasFromDisk(bigImageId, zoomLevel);
            if (loadedTex != null)
            {
                _atlasCache[key] = loadedTex;
            }
            return loadedTex;
        }

        private Texture2D LoadAtlasFromDisk(int bigImageId, int zoomLevel)
        {
            // Trường hợp x2, x3, x4: Đọc file PNG raw bytes trực tiếp từ disk
            if (zoomLevel >= 2)
            {
                string path = Path.Combine(ServerRootPath, "data", "res", $"x{zoomLevel}", $"Big{bigImageId}");
                if (File.Exists(path))
                {
                    return CreatePointTextureFromPngBytes(File.ReadAllBytes(path), $"Big{bigImageId}_x{zoomLevel}");
                }
            }

            // Trường hợp x1: Server không có thư mục Big trong res/x1/.
            // Thực hiện chiến lược Downsampling từ x2 (scale 0.5) hoặc x4 (scale 0.25).
            if (zoomLevel == 1)
            {
                string pathX1 = Path.Combine(ServerRootPath, "data", "res", "x1", $"Big{bigImageId}");
                if (File.Exists(pathX1))
                {
                    return CreatePointTextureFromPngBytes(File.ReadAllBytes(pathX1), $"Big{bigImageId}_x1");
                }

                // Downsample từ x2 (510x510 -> 255x255)
                Texture2D texX2 = GetAtlasTexture(bigImageId, 2);
                if (texX2 != null)
                {
                    Texture2D texX1 = DownsampleTexture(texX2, 2);
                    if (texX1 != null) texX1.name = $"Big{bigImageId}_x1_from_x2";
                    return texX1;
                }

                // Fallback: Downsample từ x4 (1020x1020 -> 255x255)
                Texture2D texX4 = GetAtlasTexture(bigImageId, 4);
                if (texX4 != null)
                {
                    Texture2D texX1 = DownsampleTexture(texX4, 4);
                    if (texX1 != null) texX1.name = $"Big{bigImageId}_x1_from_x4";
                    return texX1;
                }
            }

            return null;
        }

        /// <summary>
        /// Nạp Texture2D từ PNG bytes và thiết lập chế độ Point Filter (không mờ nét pixel).
        /// </summary>
        private static Texture2D CreatePointTextureFromPngBytes(byte[] pngBytes, string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(tex, pngBytes))
            {
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.name = name;
                return tex;
            }
            Destroy(tex);
            return null;
        }

        /// <summary>
        /// Thu nhỏ Texture2D theo tỷ lệ nguyên (factor = 2 hoặc 4) bằng thuật toán Point Sampling,
        /// bảo toàn nguyên vẹn độ sắc nét của đồ họa pixel NRO.
        /// </summary>
        public static Texture2D DownsampleTexture(Texture2D source, int factor)
        {
            if (source == null || factor <= 1) return source;

            int dstW = source.width / factor;
            int dstH = source.height / factor;
            if (dstW <= 0 || dstH <= 0) return null;

            Color32[] srcPixels = source.GetPixels32();
            Color32[] dstPixels = new Color32[dstW * dstH];
            int srcW = source.width;

            for (int y = 0; y < dstH; y++)
            {
                int srcY = y * factor;
                int dstRow = y * dstW;
                int srcRow = srcY * srcW;
                for (int x = 0; x < dstW; x++)
                {
                    int srcX = x * factor;
                    dstPixels[dstRow + x] = srcPixels[srcRow + srcX];
                }
            }

            Texture2D dstTex = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
            dstTex.SetPixels32(dstPixels);
            dstTex.Apply(false, false);
            dstTex.filterMode = FilterMode.Point;
            dstTex.wrapMode = TextureWrapMode.Clamp;
            return dstTex;
        }

        #endregion

        #region Core 2-Branch Sprite Resolution (Features 4, 5 & 6)

        /// <summary>
        /// Hàm giải quyết Sprite chính: Kết hợp 2 nhánh tra cứu Atlas vs Icon rời,
        /// áp dụng in-memory caching và bảo đảm PixelsPerUnit chuẩn xác.
        /// </summary>
        public Sprite ResolveSprite(int iconId, int zoomLevel = 4)
        {
            if (iconId <= 0) return null;

            zoomLevel = Mathf.Clamp(zoomLevel, 1, 4);
            var cacheKey = (iconId, zoomLevel);

            // Bước 1: Tra cứu bộ nhớ đệm
            if (_spriteCache.TryGetValue(cacheKey, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite result = null;

            // Bước 2: NHÁNH 1 (Branch 1 - Atlas Slicing)
            // Áp dụng khi iconId < 2997 VÀ tọa độ nằm trọn vẹn trong Atlas (x+w <= 255, y+h <= 255)
            if (_imageRecords != null && iconId < _imageRecords.Length && _imageRecords[iconId].IsAtlasCandidate)
            {
                TryResolveAtlas(iconId, zoomLevel, out result);
            }

            // Bước 3: NHÁNH 2 (Branch 2 - Loose PNG Icon)
            // Áp dụng khi iconId >= 2997, HOẶC tọa độ dummy >= 256, HOẶC cắt Atlas thất bại
            if (result == null)
            {
                TryResolveLooseIcon(iconId, zoomLevel, out result);
            }

            // Bước 4: Dự phòng tuyệt đối (Zero Crash Fallback)
            if (result == null)
            {
                result = CreateDummyFallbackSprite(iconId, zoomLevel);
            }

            // Lưu vào cache và trả về
            _spriteCache[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// Nhánh 1: Cắt Sprite từ Texture Atlas Big0..Big4 dựa trên thông số hình học của update_data/image.
        /// Chuyển đổi tọa độ Top-Left NRO sang Bottom-Left Unity.
        /// </summary>
        public bool TryResolveAtlas(int iconId, int zoomLevel, out Sprite sprite)
        {
            sprite = null;
            if (_imageRecords == null || iconId < 0 || iconId >= _imageRecords.Length)
            {
                return false;
            }

            ImageRecord rec = _imageRecords[iconId];
            if (!rec.IsAtlasCandidate) return false;

            Texture2D atlasTex = GetAtlasTexture(rec.bigImageId, zoomLevel);
            if (atlasTex == null) return false;

            int z = zoomLevel;
            int atlasH = atlasTex.height;

            // Công thức chuyển đổi tọa độ hệ trục:
            // NRO: (0,0) tại Top-Left. Unity: (0,0) tại Bottom-Left.
            float sliceX = rec.x * z;
            float sliceW = rec.w * z;
            float sliceH = rec.h * z;
            float sliceY = atlasH - (rec.y + rec.h) * z;

            // Kiểm tra an toàn biên trước khi gọi Sprite.Create
            if (sliceX < 0f || sliceY < 0f || sliceX + sliceW > atlasTex.width || sliceY + sliceH > atlasTex.height)
            {
                return false;
            }

            Rect rect = new Rect(sliceX, sliceY, sliceW, sliceH);
            Vector2 pivot = Vector2.zero; // Bottom-Left pivot chuẩn
            float ppu = 100f * zoomLevel; // PPU chuẩn theo tỷ lệ zoom

            sprite = Sprite.Create(atlasTex, rect, pivot, ppu);
            sprite.name = $"Atlas_{rec.bigImageId}_{iconId}_x{zoomLevel}";
            return true;
        }

        /// <summary>
        /// Nhánh 2: Nạp tệp PNG rời từ SRC/data/icon/x{zoom}/<iconId>.png.
        /// Tự động tìm kiếm chéo sang các độ phân giải khác (x4, x2, x3, x1) nếu cấp zoom yêu cầu bị khuyết icon.
        /// </summary>
        public bool TryResolveLooseIcon(int iconId, int zoomLevel, out Sprite sprite)
        {
            sprite = null;
            if (iconId <= 0) return false;

            string primaryPath = Path.Combine(ServerRootPath, "data", "icon", $"x{zoomLevel}", $"{iconId}.png");
            string targetPath = primaryPath;
            int actualZoom = zoomLevel;

            // Cơ chế tìm kiếm chéo (Cross-Zoom Fallback)
            if (!File.Exists(targetPath))
            {
                int[] fallbackOrder = { 4, 2, 3, 1 };
                foreach (int fbZoom in fallbackOrder)
                {
                    if (fbZoom == zoomLevel) continue;
                    string fbPath = Path.Combine(ServerRootPath, "data", "icon", $"x{fbZoom}", $"{iconId}.png");
                    if (File.Exists(fbPath))
                    {
                        targetPath = fbPath;
                        actualZoom = fbZoom;
                        break;
                    }
                }
            }

            if (!File.Exists(targetPath)) return false;

            try
            {
                byte[] rawBytes = File.ReadAllBytes(targetPath);
                Texture2D tex = CreatePointTextureFromPngBytes(rawBytes, $"Icon_{iconId}_x{actualZoom}");
                if (tex == null) return false;

                Rect rect = new Rect(0f, 0f, tex.width, tex.height);
                Vector2 pivot = Vector2.zero;

                // Gán PPU theo đúng actualZoom của texture nạp được để bảo toàn kích thước world
                float ppu = 100f * actualZoom;

                sprite = Sprite.Create(tex, rect, pivot, ppu);
                sprite.name = $"Loose_{iconId}_x{actualZoom}";
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NroSpriteResolver] Lỗi khi tạo Sprite từ loose PNG ({targetPath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tạo Sprite trong suốt 1x1 pixel dự phòng cho trường hợp icon bị mất hoàn toàn,
        /// ngăn chặn mọi ngoại lệ NullReferenceException trong vòng lặp Render.
        /// </summary>
        public Sprite CreateDummyFallbackSprite(int iconId, int zoomLevel)
        {
            if (_dummyFallbackSprite == null)
            {
                Texture2D dummyTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                dummyTex.SetPixel(0, 0, new Color(0, 0, 0, 0));
                dummyTex.Apply();
                dummyTex.filterMode = FilterMode.Point;
                _dummyFallbackSprite = Sprite.Create(dummyTex, new Rect(0, 0, 1, 1), Vector2.zero, 100f);
                _dummyFallbackSprite.name = "Dummy_Transparent_Sprite";
            }
            return _dummyFallbackSprite;
        }

        #endregion

        #region Part & Frame Integration (Interface với NroMannequin)

        /// <summary>
        /// Tạo đối tượng NroPartData lúc runtime từ danh sách khung hình thô trích xuất từ database.
        /// </summary>
        public NroPartData ResolvePartData(int partId, NroSlot slot, int[][] rawFrames, int zoomLevel = 4)
        {
            var part = ScriptableObject.CreateInstance<NroPartData>();
            part.partId = partId;
            part.slot = slot;

            if (rawFrames != null && rawFrames.Length > 0)
            {
                part.frames = new NroPartFrame[rawFrames.Length];
                for (int i = 0; i < rawFrames.Length; i++)
                {
                    int iconId = rawFrames[i][0];
                    int dx = rawFrames[i][1];
                    int dy = rawFrames[i][2];

                    part.frames[i] = new NroPartFrame
                    {
                        sprite = ResolveSprite(iconId, zoomLevel),
                        dx = dx,
                        dy = dy
                    };
                }
            }
            else
            {
                part.frames = Array.Empty<NroPartFrame>();
            }

            return part;
        }

        #endregion

        #region Memory Management

        public void ClearSpriteCache()
        {
            foreach (var kvp in _spriteCache)
            {
                if (kvp.Value != null)
                {
                    if (kvp.Value.texture != null && !kvp.Value.name.StartsWith("Atlas_"))
                    {
                        Destroy(kvp.Value.texture);
                    }
                    Destroy(kvp.Value);
                }
            }
            _spriteCache.Clear();
        }

        public void ClearAtlasCache()
        {
            foreach (var kvp in _atlasCache)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }
            _atlasCache.Clear();
        }

        public void ClearAllCache()
        {
            ClearSpriteCache();
            ClearAtlasCache();
        }

        public void Dispose()
        {
            ClearAllCache();
            if (_dummyFallbackSprite != null)
            {
                if (_dummyFallbackSprite.texture != null) Destroy(_dummyFallbackSprite.texture);
                Destroy(_dummyFallbackSprite);
                _dummyFallbackSprite = null;
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }

        #endregion
    }
}
