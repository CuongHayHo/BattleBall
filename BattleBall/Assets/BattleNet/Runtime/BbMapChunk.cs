using System.Collections.Generic;
using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// One map of the world as the client draws it: its size, the colours of the sky and the ground, the scenery and the NPCs standing on it.
    /// There are no real map chunks on the client yet, so every map id is served by <see cref="BbPlaceholderMaps"/> (a drawn village with the NPCs of the
    /// starter village). Real chunks replace it by setting <see cref="BbMaps.Provider"/> to a provider that reads them; the village view only
    /// asks the provider for a chunk by id and draws what it gets (docs/architecture/BattleBallServer.md, menu and village).
    /// </summary>
    public sealed class BbMapChunk
    {
        public string Id;
        public string Name;
        public float Width = 2600f;                  // canvas pixels
        public float GroundHeight = 150f;            // height of the ground strip, the characters stand on its top edge
        public float SpawnX = 900f;
        public Color SkyTop = new Color(0.35f, 0.62f, 0.92f), SkyBottom = new Color(0.78f, 0.90f, 0.98f);
        public Color GroundTop = new Color(0.36f, 0.62f, 0.22f), Ground = new Color(0.52f, 0.38f, 0.22f);
        public readonly List<Scenery> Scenery = new List<Scenery>();
        public readonly List<NpcSpec> Npcs = new List<NpcSpec>();
        public bool IsPlaceholder;

        // ---- a map drawn from real chunk pictures (BbChunkMaps); a placeholder map leaves these empty and has flat ground
        public float UiScale = 2.2f;                      // canvas pixels per Nro pixel
        public float WorldHeight;                         // canvas pixels from the bottom of the picture to the top of the sky; 0 = as high as the screen
        public float SegmentWidth;                        // canvas pixels of one picture of the map
        public int Segments = 1;                          // pictures joined end to end
        public bool FlipOdd;                              // every second picture is mirrored
        public float[] Terrain;                           // ground height (canvas pixels above the bottom of the picture) every TerrainStep canvas pixels along one picture
        public float TerrainStep = 1f;
        public readonly List<GroundSlice> GroundSlices = new List<GroundSlice>();
        public readonly List<DecorPiece> Decor = new List<DecorPiece>();
        public BackgroundInfo Background;                 // the far picture behind the map (mountains), or null

        public bool HasPictures { get { return GroundSlices.Count > 0; } }

        /// <summary>Height of the ground the characters stand on at this x (canvas pixels above the bottom of the world).</summary>
        public float GroundAt(float x)
        {
            if (Terrain == null || Terrain.Length < 2 || SegmentWidth <= 0f) return GroundHeight - 6f;
            int seg = Mathf.Clamp(Mathf.FloorToInt(x / SegmentWidth), 0, Segments - 1);
            float local = x - seg * SegmentWidth;
            if (FlipOdd && seg % 2 == 1) local = SegmentWidth - local;
            float f = Mathf.Clamp(local / TerrainStep, 0f, Terrain.Length - 1.001f);
            int i = (int)f;
            return Mathf.Lerp(Terrain[i], Terrain[i + 1], f - i);
        }
    }

    /// <summary>The far background: one wide picture (two halves, copies mirrored to go on) that scrolls slower than the map.</summary>
    public sealed class BackgroundInfo
    {
        public readonly List<GroundSlice> Slices = new List<GroundSlice>();     // the halves of one copy, X from the left of the copy
        public float CopyWidth, Height;                   // canvas pixels
        public float Parallax = 0.25f;
        public float BaseY;                               // height of the world where the bottom of the picture sits
        public Color Top, Bottom;                         // average colour of the top and of the bottom row: the sky above and the fill below
    }

    /// <summary>A part of one picture of the map: a sprite and where it sits inside the picture (canvas pixels from its bottom left).</summary>
    public struct GroundSlice
    {
        public Sprite Sprite;
        public float X, Width, Height;
    }

    /// <summary>A tree, a house or a rock: a sprite whose bottom centre stands at (X, Y) of the world.</summary>
    public struct DecorPiece
    {
        public Sprite Sprite;
        public float X, Y, Width, Height;
    }

    public enum SceneryKind { Hill, Cloud, House, Tree, Fence, Sign }

    public struct Scenery
    {
        public SceneryKind Kind;
        public float X, Width, Height;
        public float Parallax;                       // 1 = moves with the ground, 0.5 = far layer
        public Color Color;
    }

    public enum NpcAction { Talk, DungeonGate, TrainingDummy, Shop, Smith }          // Shop: the server sends what the NPC sells (docs 15.15)

    public sealed class NpcSpec
    {
        public string Id, Name, Talk;
        public int CostumeId;                        // a costume of the Nro catalog used as the NPC's picture (placeholder until NPC templates exist)
        public float X;
        public NpcAction Action;
        public int MobId = -1;                       // TrainingDummy drawn as an Nro mob (0 = Mộc nhân); -1 = drawn with CostumeId; the Id is the key of balance_config training
    }

    /// <summary>Where the village view gets its maps.</summary>
    public interface IBbMapProvider
    {
        BbMapChunk Load(string chunkId);
    }

    public static class BbMaps
    {
        public const string StarterVillage = "village_start";

        /// <summary>Replace it with a provider that reads real chunks.</summary>
        public static IBbMapProvider Provider = new BbChunkMaps(new BbPlaceholderMaps());
    }

    /// <summary>The stand-in for the map files that do not exist yet: every id gives the drawn starter village.</summary>
    public sealed class BbPlaceholderMaps : IBbMapProvider
    {
        public BbMapChunk Load(string chunkId)
        {
            var map = new BbMapChunk { Id = chunkId, Name = "Làng tân thủ", IsPlaceholder = true };
            var rng = new System.Random(7);
            // far hills and clouds (move slower than the ground)
            for (int i = 0; i < 6; i++)
                map.Scenery.Add(new Scenery { Kind = SceneryKind.Hill, X = 150 + i * 520, Width = 620 + (i % 3) * 120, Height = 170 + (i % 2) * 70, Parallax = 0.35f, Color = new Color(0.42f, 0.62f, 0.50f) });
            for (int i = 0; i < 7; i++)
                map.Scenery.Add(new Scenery { Kind = SceneryKind.Cloud, X = 100 + i * 420, Width = 170 + rng.Next(80), Height = 54, Parallax = 0.2f, Color = new Color(1f, 1f, 1f, 0.85f) });
            // houses, trees, fences on the ground
            map.Scenery.Add(new Scenery { Kind = SceneryKind.House, X = 330, Width = 230, Height = 190, Parallax = 1f, Color = new Color(0.92f, 0.82f, 0.62f) });
            map.Scenery.Add(new Scenery { Kind = SceneryKind.House, X = 1020, Width = 260, Height = 210, Parallax = 1f, Color = new Color(0.86f, 0.70f, 0.52f) });
            map.Scenery.Add(new Scenery { Kind = SceneryKind.House, X = 1680, Width = 240, Height = 200, Parallax = 1f, Color = new Color(0.90f, 0.78f, 0.60f) });
            map.Scenery.Add(new Scenery { Kind = SceneryKind.House, X = 2290, Width = 230, Height = 190, Parallax = 1f, Color = new Color(0.82f, 0.68f, 0.50f) });
            foreach (float x in new float[] { 120, 640, 860, 1330, 1520, 2060, 2480 })
                map.Scenery.Add(new Scenery { Kind = SceneryKind.Tree, X = x, Width = 120, Height = 190 + (int)(x % 3) * 25, Parallax = 1f, Color = new Color(0.20f, 0.52f, 0.20f) });
            foreach (float x in new float[] { 560, 1190, 1900, 2150 })
                map.Scenery.Add(new Scenery { Kind = SceneryKind.Fence, X = x, Width = 220, Height = 46, Parallax = 1f, Color = new Color(0.55f, 0.36f, 0.18f) });
            // the NPCs of the starter village (pictures are placeholders: costumes of the catalog)
            map.Npcs.Add(new NpcSpec { Id = "elder", Name = "Ông Trưởng làng", CostumeId = 430, X = 1300, Action = NpcAction.Talk,
                Talk = "Chào cháu! Dùng cần điều khiển để đi lại. Hãy mở menu bên trái để xem nhân vật, hành trang và hòm thư." });
            map.Npcs.Add(new NpcSpec { Id = "shop", Name = "Bà Hạt Mít", CostumeId = 9022, X = 1850, Action = NpcAction.Smith, Talk = "" });          // upgrades gear (docs 15.22)
            map.Npcs.Add(new NpcSpec { Id = "cui", Name = "Cui", CostumeId = 9023, X = 2300, Action = NpcAction.DungeonGate,
                Talk = "Ta đưa cháu tới các phó bản. Chức năng này sắp có." });
            // Bunma sells clothes and stones (docs 15.17)
            map.Npcs.Add(new NpcSpec { Id = "bunma", Name = "Bunma", CostumeId = 9021, X = 700, Action = NpcAction.Shop, Talk = "" });
            // Urôn sells every skin (docs 15.15)
            map.Npcs.Add(new NpcSpec { Id = "uron", Name = "Urôn", CostumeId = 9020, X = 1550, Action = NpcAction.Shop, Talk = "" });
            // the training dummies (docs/architecture/BattleBallServer.md 15.12)
            map.Npcs.Add(new NpcSpec { Id = "moc_nhan", Name = "Mộc nhân", MobId = 0, X = 560, Action = NpcAction.TrainingDummy, Talk = "" });
            map.Npcs.Add(new NpcSpec { Id = "saibamen", Name = "Saibamen", CostumeId = 647, X = 760, Action = NpcAction.TrainingDummy, Talk = "" });
            return map;
        }
    }
}
