using System;
using System.Collections.Generic;
using System.IO;
using NroRig;
using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// Reads a map chunk made by BattleBallServer/tools/build_village_map.py from the asset folder (SourceData in the Editor, the downloaded cache on a device):
    /// data/map/&lt;id&gt;/chunk.json, the ground pictures ground_a_x{N}.png and ground_b_x{N}.png and the scenery pictures bg/x{N}/&lt;id&gt;.png of the scale in use.
    /// A map whose files are missing is served by the fallback provider (the drawn placeholder village).
    /// </summary>
    public sealed class BbChunkMaps : IBbMapProvider
    {
        readonly IBbMapProvider _fallback;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        public BbChunkMaps(IBbMapProvider fallback) { _fallback = fallback; }

        [Serializable]
        class ChunkFile
        {
            public string id, name;
            public float segmentWidth, imageHeight, spawnX;
            public int segments, groundStep;
            public bool flipOdd;
            public float[] sliceX, sliceW, ground;
            public DecorFile[] decor;
            public NpcFile[] npcs;
            public BackgroundFile background;
        }

        [Serializable]
        class BackgroundFile { public float width, height, heightCanvas, parallax, baseY; public float[] top, bottom; }

        [Serializable]
        class DecorFile { public int id; public float x, ground; }

        [Serializable]
        class NpcFile { public string id, name, talk, action; public int costume; public float x; public int mob = -1; }

        /// <summary>Destroys the textures of the map that was loaded before (a new load, for example after the scale changed).</summary>
        public void Release()
        {
            foreach (var o in _owned) if (o != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
            _owned.Clear();
        }

        public BbMapChunk Load(string chunkId)
        {
            Release();
            try
            {
                string dir = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "map", chunkId);
                string file = Path.Combine(dir, "chunk.json");
                if (!File.Exists(file)) return _fallback.Load(chunkId);
                var f = JsonUtility.FromJson<ChunkFile>(File.ReadAllText(file));
                if (f == null || f.ground == null || f.ground.Length < 2 || f.sliceX == null) return _fallback.Load(chunkId);

                int scale = BbAssetCache.Scale;
                float ui = 2.2f;
                var map = new BbMapChunk
                {
                    Id = chunkId, Name = f.name, UiScale = ui, SegmentWidth = f.segmentWidth * ui, Segments = Mathf.Max(1, f.segments), FlipOdd = f.flipOdd,
                    Width = f.segmentWidth * ui * Mathf.Max(1, f.segments), WorldHeight = f.imageHeight * ui + 520f, GroundHeight = 330f, SpawnX = f.spawnX * ui,
                    TerrainStep = Mathf.Max(1, f.groundStep) * ui,
                    SkyTop = new Color(0.35f, 0.62f, 0.92f), SkyBottom = new Color(0.78f, 0.90f, 0.98f)
                };
                map.Terrain = new float[f.ground.Length];
                for (int i = 0; i < f.ground.Length; i++) map.Terrain[i] = f.ground[i] * ui;

                string[] names = { "ground_a", "ground_b" };
                for (int i = 0; i < f.sliceX.Length && i < names.Length; i++)
                {
                    var sprite = LoadSprite(Path.Combine(dir, names[i] + "_x{0}.png"), scale, true);
                    if (sprite == null) return _fallback.Load(chunkId);          // the pictures of this map are not here: show the placeholder
                    map.GroundSlices.Add(new GroundSlice { Sprite = sprite, X = f.sliceX[i] * ui, Width = f.sliceW[i] * ui, Height = f.imageHeight * ui });
                }

                var cache = new Dictionary<int, Sprite>();
                if (f.decor != null)
                    foreach (var d in f.decor)
                    {
                        Sprite sp;
                        if (!cache.TryGetValue(d.id, out sp))
                        {
                            int used;
                            sp = LoadSprite(Path.Combine(dir, "bg", "x{0}", d.id + ".png"), scale, false, out used);
                            cache[d.id] = sp;
                            if (sp != null) sp.name = "bg" + d.id + "_x" + used;
                        }
                        if (sp == null) continue;
                        int u = int.Parse(sp.name.Substring(sp.name.LastIndexOf('x') + 1));
                        float w = sp.rect.width / u * ui, h = sp.rect.height / u * ui;
                        map.Decor.Add(new DecorPiece { Sprite = sp, X = d.x * ui, Y = d.ground * ui - 5f * ui, Width = w, Height = h });
                    }

                if (f.npcs != null)
                    foreach (var n in f.npcs)
                        map.Npcs.Add(new NpcSpec { Id = n.id, Name = n.name, Talk = n.talk, CostumeId = n.costume, X = n.x * ui, MobId = n.mob,
                            Action = n.action == "DungeonGate" ? NpcAction.DungeonGate : n.action == "TrainingDummy" ? NpcAction.TrainingDummy : NpcAction.Talk });

                var bgf = f.background;
                if (bgf != null && bgf.width > 0f && bgf.top != null && bgf.top.Length == 3 && bgf.bottom != null && bgf.bottom.Length == 3)
                {
                    var bg = new BackgroundInfo
                    {
                        Height = bgf.heightCanvas, CopyWidth = bgf.heightCanvas * bgf.width / bgf.height, Parallax = bgf.parallax, BaseY = bgf.baseY,
                        Top = new Color(bgf.top[0], bgf.top[1], bgf.top[2]), Bottom = new Color(bgf.bottom[0], bgf.bottom[1], bgf.bottom[2])
                    };
                    string[] halves = { "background_a", "background_b" };
                    for (int i = 0; i < halves.Length; i++)
                    {
                        var sprite = LoadSprite(Path.Combine(dir, halves[i] + "_x{0}.jpg"), scale, true);
                        if (sprite == null) { bg = null; break; }
                        bg.Slices.Add(new GroundSlice { Sprite = sprite, X = bg.CopyWidth * 0.5f * i, Width = bg.CopyWidth * 0.5f, Height = bg.Height });
                    }
                    map.Background = bg;
                    if (bg != null)
                    {
                        map.SkyTop = bg.Top;
                        map.SkyBottom = Color.Lerp(bg.Top, Color.white, 0.35f);
                    }
                }
                if (map.Background == null) AddFarScenery(map);              // no background picture: the drawn hills and clouds
                return map;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BbChunkMaps] cannot read the map " + chunkId + ": " + e.Message);
                return _fallback.Load(chunkId);
            }
        }

        /// <summary>The far hills and clouds behind the picture (they scroll slower than the ground).</summary>
        static void AddFarScenery(BbMapChunk map)
        {
            var rng = new System.Random(11);
            for (float x = 0; x < map.Width * 0.35f + 1800f; x += 520f)
                map.Scenery.Add(new Scenery { Kind = SceneryKind.Hill, X = x, Width = 700 + rng.Next(240), Height = 190 + rng.Next(90), Parallax = 0.35f, Color = new Color(0.45f, 0.65f, 0.55f) });
            for (float x = 0; x < map.Width * 0.2f + 1800f; x += 420f)
                map.Scenery.Add(new Scenery { Kind = SceneryKind.Cloud, X = x + rng.Next(120), Width = 170 + rng.Next(90), Height = 54, Parallax = 0.2f, Color = new Color(1f, 1f, 1f, 0.85f) });
        }

        /// <summary>A sprite from a PNG whose name carries the scale ({0}); when the file of this scale is missing another scale is used.</summary>
        Sprite LoadSprite(string pattern, int scale, bool smooth)
        {
            int used;
            return LoadSprite(pattern, scale, smooth, out used);
        }

        Sprite LoadSprite(string pattern, int scale, bool smooth, out int usedScale)
        {
            int[] order = { scale, 4, 3, 2, 1 };
            foreach (int s in order)
            {
                string path = string.Format(pattern, s);
                if (!File.Exists(path)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) { UnityEngine.Object.DestroyImmediate(tex); continue; }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = smooth ? FilterMode.Bilinear : FilterMode.Bilinear;
                _owned.Add(tex);
                usedScale = s;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, 100f);
                _owned.Add(sprite);
                return sprite;
            }
            usedScale = scale;
            return null;
        }
    }
}
