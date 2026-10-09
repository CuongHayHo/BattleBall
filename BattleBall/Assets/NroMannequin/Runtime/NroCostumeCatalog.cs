// =========================================================================================
// File: Assets/NroMannequin/Runtime/NroCostumeCatalog.cs
// Description: Runtime In-Memory Database and Search/Filter Provider for NRO Costumes and Parts.
// =========================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NroRig
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class NroCostumeCatalog : MonoBehaviour
    {
        private static NroCostumeCatalog _instance;
        public static NroCostumeCatalog Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<NroCostumeCatalog>();
                    if (_instance == null)
                    {
                        var go = new GameObject("NroCostumeCatalog");
                        _instance = go.AddComponent<NroCostumeCatalog>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Tùy chọn TextAsset (Inspector gán sẵn)")]
        [SerializeField] private TextAsset costumeCatalogAsset;
        [SerializeField] private TextAsset partDatabaseAsset;

        [Header("Đường dẫn Fallback")]
        [SerializeField] private string resourcesCostumePath = "CostumeCatalog";
        [SerializeField] private string resourcesPartPath = "PartDatabase";

        private readonly Dictionary<int, NroCostumeEntry> _costumesById = new Dictionary<int, NroCostumeEntry>();
        private readonly Dictionary<int, NroPartEntry> _partsById = new Dictionary<int, NroPartEntry>();
        private readonly List<NroCostumeEntry> _allCostumes = new List<NroCostumeEntry>();
        private readonly Dictionary<NroCostumeGroup, List<NroCostumeEntry>> _costumesByGroup = 
            new Dictionary<NroCostumeGroup, List<NroCostumeEntry>>();

        // Cache runtime ScriptableObject NroPartData sinh theo zoom: key = (partId, zoom)
        private readonly Dictionary<(int partId, int zoom), NroPartData> _runtimePartCache = 
            new Dictionary<(int, int), NroPartData>();

        public bool IsLoaded { get; private set; }

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

        public void Initialize()
        {
            if (IsLoaded) return;

            string costumeJson = LoadJsonString(costumeCatalogAsset, resourcesCostumePath, "CostumeCatalog.json");
            string partJson = LoadJsonString(partDatabaseAsset, resourcesPartPath, "PartDatabase.json");

            if (string.IsNullOrEmpty(costumeJson) || string.IsNullOrEmpty(partJson))
            {
                Debug.LogError("[NroCostumeCatalog] Không thể tải dữ liệu JSON!");
                return;
            }

            ParseCostumes(costumeJson);
            ParseParts(partJson);

            IsLoaded = true;
            Debug.Log($"[NroCostumeCatalog] Khởi tạo thành công: {_allCostumes.Count} Costumes, {_partsById.Count} Parts.");
        }

        private string LoadJsonString(TextAsset asset, string resourcePath, string fileName)
        {
            // 1. TextAsset từ Inspector
            if (asset != null) return asset.text;

            // 2. Direct Project Assets Data Path (Editor / Standalone)
            string directPath = Path.Combine(Application.dataPath, "NroMannequin", "Data", fileName);
            if (File.Exists(directPath)) return File.ReadAllText(directPath);

            // 3. Resources.Load
            TextAsset resAsset = Resources.Load<TextAsset>(resourcePath);
            if (resAsset != null) return resAsset.text;

            // 4. StreamingAssets
            string saPath = Path.Combine(Application.streamingAssetsPath, fileName);
            if (File.Exists(saPath)) return File.ReadAllText(saPath);

            Debug.LogWarning($"[NroCostumeCatalog] Không tìm thấy tệp {fileName} ở bất kỳ vị trí nạp nào!");
            return null;
        }

        private void ParseCostumes(string json)
        {
            NroCostumeCatalogData data = JsonUtility.FromJson<NroCostumeCatalogData>(json);
            if (data == null || data.costumes == null) return;

            _costumesById.Clear();
            _allCostumes.Clear();
            _costumesByGroup.Clear();

            foreach (NroCostumeGroup g in Enum.GetValues(typeof(NroCostumeGroup)))
            {
                _costumesByGroup[g] = new List<NroCostumeEntry>();
            }

            for (int i = 0; i < data.costumes.Count; i++)
            {
                NroCostumeEntry c = data.costumes[i];
                if (c == null) continue;

                _costumesById[c.id] = c;
                _allCostumes.Add(c);

                NroCostumeGroup grp = c.CostumeGroup;
                if (_costumesByGroup.ContainsKey(grp))
                {
                    _costumesByGroup[grp].Add(c);
                }
            }
        }

        private void ParseParts(string json)
        {
            NroPartDatabaseData data = JsonUtility.FromJson<NroPartDatabaseData>(json);
            if (data == null || data.parts == null) return;

            _partsById.Clear();
            for (int i = 0; i < data.parts.Count; i++)
            {
                NroPartEntry p = data.parts[i];
                if (p != null)
                {
                    _partsById[p.partId] = p;
                }
            }
        }

        // ==================== PUBLIC LOOKUP APIS ====================

        public IReadOnlyList<NroCostumeEntry> GetAllCostumes()
        {
            if (!IsLoaded) Initialize();
            return _allCostumes;
        }

        public bool TryGetCostume(int costumeId, out NroCostumeEntry costume)
        {
            if (!IsLoaded) Initialize();
            return _costumesById.TryGetValue(costumeId, out costume);
        }

        public NroCostumeEntry GetCostume(int costumeId)
        {
            if (!IsLoaded) Initialize();
            _costumesById.TryGetValue(costumeId, out var c);
            return c;
        }

        public bool TryGetPart(int partId, out NroPartEntry part)
        {
            if (!IsLoaded) Initialize();
            return _partsById.TryGetValue(partId, out part);
        }

        public NroPartEntry GetPartEntry(int partId)
        {
            if (!IsLoaded) Initialize();
            _partsById.TryGetValue(partId, out var p);
            return p;
        }

        public IReadOnlyList<NroCostumeEntry> FilterByGroup(NroCostumeGroup group)
        {
            if (!IsLoaded) Initialize();
            if (_costumesByGroup.TryGetValue(group, out var list))
                return list;
            return Array.Empty<NroCostumeEntry>();
        }

        public List<NroCostumeEntry> SearchCostumes(string query)
        {
            return SearchAndFilter(query, null);
        }

        public List<NroCostumeEntry> SearchAndFilter(string query, NroCostumeGroup? groupFilter)
        {
            if (!IsLoaded) Initialize();

            List<NroCostumeEntry> result = new List<NroCostumeEntry>();
            bool hasQuery = !string.IsNullOrWhiteSpace(query);
            string q = hasQuery ? query.Trim().ToLowerInvariant() : null;

            IReadOnlyList<NroCostumeEntry> source = groupFilter.HasValue 
                ? FilterByGroup(groupFilter.Value) 
                : _allCostumes;

            for (int i = 0; i < source.Count; i++)
            {
                NroCostumeEntry c = source[i];
                if (c == null) continue;

                if (hasQuery)
                {
                    bool matchId = c.id.ToString().Contains(q);
                    bool matchName = !string.IsNullOrEmpty(c.name) && c.name.ToLowerInvariant().Contains(q);
                    bool matchHead = c.headPartId >= 0 && c.headPartId.ToString().Contains(q);
                    bool matchBody = c.bodyPartId >= 0 && c.bodyPartId.ToString().Contains(q);
                    bool matchLeg = c.legPartId >= 0 && c.legPartId.ToString().Contains(q);
                    if (!matchId && !matchName && !matchHead && !matchBody && !matchLeg) continue;
                }

                result.Add(c);
            }

            return result;
        }

        // ==================== RUNTIME PART INSTANTIATION ====================

        public NroPartData GetPart(int partId, int zoomLevel)
        {
            return GetOrCreateRuntimePart(partId, zoomLevel, NroSpriteResolver.Instance);
        }

        /// <summary>
        /// Tạo hoặc lấy từ Cache đối tượng NroPartData tương ứng với partId và zoomLevel,
        /// sử dụng NroSpriteResolver để phân giải sprite cho từng frame.
        /// </summary>
        public NroPartData GetOrCreateRuntimePart(int partId, int zoomLevel, INroSpriteResolver resolver)
        {
            if (partId < 0) return null;
            if (!IsLoaded) Initialize();

            var key = (partId, zoomLevel);
            if (_runtimePartCache.TryGetValue(key, out var cachedData) && cachedData != null)
            {
                return cachedData;
            }

            if (!TryGetPart(partId, out var partEntry) || partEntry.images == null)
            {
                return null;
            }

            // Tạo instance động của ScriptableObject trong bộ nhớ RAM
            NroPartData partData = ScriptableObject.CreateInstance<NroPartData>();
            partData.partId = partId;
            partData.slot = partEntry.GetSlot();

            int count = partEntry.images.Count;
            partData.frames = new NroPartFrame[count];

            for (int i = 0; i < count; i++)
            {
                var img = partEntry.images[i];
                Sprite spr = null;
                if (img.iconId > 0 && resolver != null)
                {
                    spr = resolver.ResolveSprite(img.iconId, zoomLevel);
                }

                partData.frames[i] = new NroPartFrame
                {
                    sprite = spr,
                    dx = img.dx,
                    dy = img.dy
                };
            }

            _runtimePartCache[key] = partData;
            return partData;
        }

        public void ClearPartCache()
        {
            foreach (var kvp in _runtimePartCache)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }
            _runtimePartCache.Clear();
        }

        private void OnDestroy()
        {
            ClearPartCache();
        }
    }
}
