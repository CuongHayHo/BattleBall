using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The village behind the menu: a side-scrolling map with the NPCs of the starter village, the main character and the two others standing next to it as AI
    /// followers. The joystick moves the main character (left and right, up to jump, held up in the air to fly), the followers walk after it and the view scrolls
    /// in both directions. The map comes from <see cref="BbMaps.Provider"/>: the chunk made from the NRO map 5 (ground pictures, terrain heights, trees and houses),
    /// or the drawn placeholder when its files are not there. The terrain is walkable: small steps are climbed, walls stop a walker (the player flies over them,
    /// the AI flies up the wall by itself) and a character that walks off a ledge falls. Everything is made of UI Images, so it needs no camera of its own.
    /// </summary>
    public sealed partial class BbVillageView : MonoBehaviour
    {
        public RectTransform viewport;
        public Font font;
        public Sprite whiteSprite, dotSprite;
        public BbJoystick joystick;
        [Tooltip("UI pixels per Nro pixel for the characters and the NPCs")]
        public float characterScale = 2.2f;

        /// <summary>Raised when the player taps an NPC.</summary>
        public Action<NpcSpec> NpcTapped;
        /// <summary>A training dummy was tapped (its key, the id of the map NPC and of balance_config training.dummies).</summary>
        public Action<string> DummyTapped;

        public struct ActorSpec
        {
            public ulong Id;
            public string Name;
            public int CostumeId;
            public bool Main;
        }

        sealed class Actor
        {
            public ulong id;
            public string name;
            public RectTransform root;
            public BbCharacterView view;
            public Text label;
            public int costume;
            public float lift;                   // canvas px the picture is raised above the feet (Quả cầu kênh khí: the character rises); the physics ignore it
            public int look;                     // a look worn for a while (Biến hình: the great ape); 0 = the own costume
            public float x, y, vy;               // y: the feet, canvas pixels above the bottom of the world
            public int facing = 1;
            public bool grounded = true;
            public bool flying;
            public bool climbing;                // an AI flying up a wall that blocks its way
            public bool running;                 // the AI is walking (with a dead band, so it does not flicker between run and stand)
            public float runUntil;
            public float holdUp;                 // seconds the stick has been held up in the air: long enough and the character flies
            public BbMobView mob;                // a training dummy drawn as an Nro mob (Mộc nhân)
            public bool jar;                     // a dummy that is the jar of Ma phong ba (the costume of the parts 1218 to 1220)
            public bool locked;                  // the server holds it (gong, charge, beam, stun, sleep, rope, jar): it takes no input and does not walk
            // a dungeon arena (BbVillageView.Dungeon.cs): where the server says it is (the picture follows it), the HP bar of a monster, and the throw of a body that died
            public float tx, ty;
            public bool remote;
            public bool boss;
            public Image hpFill, hpBack;
            public float hpRatio = 1f;
            public bool dying;
            public float dyingAge;
            public Vector2 dyingVelocity;
            public bool ghost;
            public string key;
        }

        // Speed: the walk of all three, doubled twice (owner requests 2026-10-08: 300, 600, now 1200); the AI follow at Speed * 1.15
        const float Speed = 1200f, Jump = 640f, Gravity = 1500f;
        // the AI stand apart from the main character and from each other (owner request 2026-10-08; the gap was 82 px between all of them)
        const float FirstFollowGap = 150f, AiFollowGap = 130f;
        // flying and sinking twice as fast (owner request 2026-10-08: fly 300 -> 600, sink 240 -> 480 -> 960); the jump keeps its height
        const float FlySpeed = 600f, FlyAfter = 0.3f, FlyBoost = 1.25f, SinkSpeed = 960f, DescentBoost = 2f;
        const float MaxStep = 36f;               // a rise of the ground up to this height is walked up, more is a wall
        const float EngageDistance = 1300f;      // the AI fight a focused dummy while the main character is this close to it (canvas px)
        const float DummyGap = 80f;              // where the AI stand beside a dummy (canvas px from its centre: 36 Nro px, inside the reach of a punch: about 30 + the dummy's 14)
        // run frames of the AI: 0.4 (a frame every 0.2 s), doubled for the AI and doubled again with the walk of all three (owner request 2026-10-08): a frame every 0.05 s
        const float AiRunAnimSpeed = 1.6f;
        const float MinRunTime = 0.6f;           // once it starts to run the AI runs at least this long, so a short catch-up still shows several steps
        const float MainAnimSpeed = 2f;          // frame speed of the main character (owner request 2026-10-08: twice as fast)
        const float MainRunAnimSpeed = 4f;       // its run frames doubled again with the walk (owner request 2026-10-08); standing keeps MainAnimSpeed

        BbMapChunk _map;
        string _mapId;
        RectTransform _sky, _world;
        readonly List<RectTransform> _layers = new List<RectTransform>();
        readonly List<float> _layerParallax = new List<float>();
        readonly List<Actor> _actors = new List<Actor>();
        readonly List<GameObject> _npcObjects = new List<GameObject>();
        readonly Dictionary<string, Actor> _npcs = new Dictionary<string, Actor>();          // the NPCs that can be talked to (not the dummies), by id: they are focus targets too
        readonly Dictionary<string, NpcSpec> _npcSpecs = new Dictionary<string, NpcSpec>();
        readonly Dictionary<string, Actor> _dummies = new Dictionary<string, Actor>();
        float _inset, _insetRight, _camX = -1f, _camY, _groupX = -1f, _clock;
        float _width = 1280f, _height = 720f;

        public BbMapChunk Map { get { return _map; } }
        public float MainX { get { return _actors.Count > 0 ? _actors[0].x : 0f; } }

        float WorldHeight { get { return _map.WorldHeight > 0f ? _map.WorldHeight : _height; } }
        float Ceiling { get { return WorldHeight - 40f; } }
        float GroundAt(float x) { return _map.GroundAt(x); }

        // ---------------------------------------------------------------------------------------------------------------- map

        /// <summary>Loads and draws this map (the chunk of the NRO map 5, or the placeholder village when the chunk files are missing).</summary>
        public void ShowMap(string chunkId)
        {
            if (_map != null && _mapId == chunkId) return;
            _mapId = chunkId;
            _map = BbMaps.Provider.Load(chunkId);
            Build();
            if (Weather == null && viewport != null)
            {
                Weather = GetComponent<BbWeather>();
                if (Weather == null) Weather = gameObject.AddComponent<BbWeather>();
                Weather.Attach(viewport);                    // sun, rain, day and night over the village
            }
        }

        public BbWeather Weather { get; private set; }

        /// <summary>Loads the map again (the pictures of another scale) and draws it; the characters are put back by the next SetCast.</summary>
        public void ReloadMap()
        {
            if (_mapId == null) return;
            _map = BbMaps.Provider.Load(_mapId);
            Build();
        }

        void ReadSize()
        {
            var r = viewport.rect;
            if (r.width > 10f && r.height > 10f) { _width = r.width; _height = r.height; }
        }

        void Build()
        {
            ReadSize();
            for (int i = viewport.childCount - 1; i >= 0; i--) Kill(viewport.GetChild(i).gameObject);
            _layers.Clear();
            _layerParallax.Clear();
            _actors.Clear();
            _npcObjects.Clear();
            _dummies.Clear();
            _npcs.Clear();
            _npcSpecs.Clear();
            _camX = -1f;
            _camY = 0f;

            _sky = NewRect("Sky", viewport);
            Stretch(_sky);
            var sky = _sky.gameObject.AddComponent<Image>();
            sky.sprite = Gradient(_map.SkyTop, _map.SkyBottom);
            sky.raycastTarget = false;
            if (_map.Flying) BuildArenaBackdrop();

            if (_map.Background != null) BuildBackground();

            // layers behind the ground: one container per parallax factor, the slowest first
            var factors = new SortedSet<float>();
            foreach (var s in _map.Scenery) if (s.Parallax < 1f) factors.Add(s.Parallax);
            foreach (float f in factors)
            {
                var layer = NewLayer("Layer" + f, viewport);
                _layers.Add(layer);
                _layerParallax.Add(f);
                foreach (var s in _map.Scenery) if (Mathf.Approximately(s.Parallax, f)) Draw(layer, s);
            }

            _world = NewLayer("World", viewport);
            _layers.Add(_world);
            _layerParallax.Add(1f);
            if (_map.HasPictures) BuildPictures();
            else
            {
                NewImage(_world, "Ground", whiteSprite, _map.Ground, 0, 0, _map.Width, _map.GroundHeight, Vector2.zero);
                NewImage(_world, "GroundTop", whiteSprite, _map.GroundTop, 0, _map.GroundHeight - 16f, _map.Width, 16f, Vector2.zero);
                foreach (var s in _map.Scenery) if (s.Parallax >= 1f) Draw(_world, s);
            }
            foreach (var npc in _map.Npcs) AddNpc(npc);
            if (_map.Flying) BuildArenaFloor();
            Layout(true);
        }

        /// <summary>
        /// The far background picture: copies of it side by side (every second one mirrored so the joints match), enough for the slow scroll, with a flat colour
        /// under its bottom edge so no sky shows between the platforms.
        /// </summary>
        void BuildBackground()
        {
            var bg = _map.Background;
            var layer = NewLayer("Background", viewport);
            _layers.Add(layer);
            _layerParallax.Add(bg.Parallax);
            int copies = Mathf.Max(1, Mathf.CeilToInt((_map.Width * bg.Parallax + _width + 400f) / bg.CopyWidth));
            for (int k = 0; k < copies; k++)
            {
                bool mirrored = k % 2 == 1;
                var copy = NewRect("Copy" + k, layer);
                copy.pivot = Vector2.zero;
                copy.sizeDelta = new Vector2(bg.CopyWidth, 10f);
                copy.anchoredPosition = new Vector2(k * bg.CopyWidth + (mirrored ? bg.CopyWidth : 0f), bg.BaseY);
                copy.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
                foreach (var slice in bg.Slices)
                    NewImage(copy, "Background", slice.Sprite, Color.white, slice.X, 0f, slice.Width, slice.Height, Vector2.zero);
            }
            NewImage(layer, "Fill", whiteSprite, bg.Bottom, -600f, -3000f, copies * bg.CopyWidth + 1200f, bg.BaseY + 3002f, Vector2.zero);
        }

        /// <summary>The pictures of the map: every segment is the same picture (two halves), every second one mirrored, with the trees and houses on it.</summary>
        void BuildPictures()
        {
            for (int s = 0; s < _map.Segments; s++)
            {
                bool mirrored = _map.FlipOdd && s % 2 == 1;
                var seg = NewRect("Segment" + s, _world);
                seg.pivot = Vector2.zero;
                seg.sizeDelta = new Vector2(_map.SegmentWidth, 10f);
                seg.anchoredPosition = new Vector2(s * _map.SegmentWidth + (mirrored ? _map.SegmentWidth : 0f), 0f);
                seg.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
                foreach (var slice in _map.GroundSlices)
                    NewImage(seg, "Ground", slice.Sprite, Color.white, slice.X, 0f, slice.Width, slice.Height, Vector2.zero);
            }
            var decor = NewRect("Decor", _world);
            decor.sizeDelta = new Vector2(10f, 10f);
            foreach (var d in _map.Decor)
                NewImage(decor, "Decor", d.Sprite, Color.white, d.X, d.Y, d.Width, d.Height, new Vector2(0.5f, 0f));
        }

        void Draw(RectTransform layer, Scenery s)
        {
            float g = _map.GroundHeight;
            switch (s.Kind)
            {
                case SceneryKind.Hill:
                    NewImage(layer, "Hill", dotSprite, s.Color, s.X, g - s.Height, s.Width, s.Height * 2f, Vector2.zero);
                    break;
                case SceneryKind.Cloud:
                    float y = _height * 0.72f + (s.X % 3) * 22f;
                    NewImage(layer, "Cloud", dotSprite, s.Color, s.X, y, s.Width, s.Height, Vector2.zero);
                    NewImage(layer, "Cloud", dotSprite, s.Color, s.X + s.Width * 0.25f, y + 18f, s.Width * 0.55f, s.Height, Vector2.zero);
                    break;
                case SceneryKind.House:
                    NewImage(layer, "House", whiteSprite, s.Color, s.X, g - 6f, s.Width, s.Height, Vector2.zero);
                    NewImage(layer, "Roof", whiteSprite, new Color(0.62f, 0.22f, 0.16f), s.X - 16f, g - 6f + s.Height, s.Width + 32f, s.Height * 0.26f, Vector2.zero);
                    NewImage(layer, "Door", whiteSprite, new Color(0.40f, 0.24f, 0.12f), s.X + s.Width * 0.38f, g - 6f, s.Width * 0.22f, s.Height * 0.5f, Vector2.zero);
                    NewImage(layer, "Window", whiteSprite, new Color(0.62f, 0.84f, 0.95f), s.X + s.Width * 0.10f, g - 6f + s.Height * 0.52f, s.Width * 0.2f, s.Height * 0.2f, Vector2.zero);
                    NewImage(layer, "Window", whiteSprite, new Color(0.62f, 0.84f, 0.95f), s.X + s.Width * 0.70f, g - 6f + s.Height * 0.52f, s.Width * 0.2f, s.Height * 0.2f, Vector2.zero);
                    break;
                case SceneryKind.Tree:
                    NewImage(layer, "Trunk", whiteSprite, new Color(0.40f, 0.26f, 0.12f), s.X + s.Width * 0.4f, g - 6f, s.Width * 0.2f, s.Height * 0.5f, Vector2.zero);
                    NewImage(layer, "Leaves", dotSprite, s.Color, s.X, g - 6f + s.Height * 0.3f, s.Width, s.Height * 0.72f, Vector2.zero);
                    break;
                case SceneryKind.Fence:
                    NewImage(layer, "Rail", whiteSprite, s.Color, s.X, g + 14f, s.Width, 8f, Vector2.zero);
                    NewImage(layer, "Rail", whiteSprite, s.Color, s.X, g + 34f, s.Width, 8f, Vector2.zero);
                    for (int i = 0; i < 6; i++) NewImage(layer, "Post", whiteSprite, s.Color, s.X + i * (s.Width - 10f) / 5f, g - 4f, 10f, s.Height, Vector2.zero);
                    break;
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- NPCs and characters

        void AddNpc(NpcSpec npc)
        {
            if (npc.Action == NpcAction.TrainingDummy) { AddDummy(npc); return; }
            var actor = NewActor(npc.Name, npc.CostumeId, new Color(1f, 0.93f, 0.55f), 20);
            actor.root.name = "Npc_" + npc.Id;
            actor.x = npc.X;
            actor.y = GroundAt(npc.X);
            var tap = actor.root.gameObject.AddComponent<Image>();               // an invisible button over the NPC
            tap.color = new Color(0f, 0f, 0f, 0f);
            var button = actor.root.gameObject.AddComponent<Button>();
            button.targetGraphic = tap;
            var spec = npc;
            button.onClick.AddListener(() => { if (NpcTapped != null) NpcTapped(spec); });
            actor.root.sizeDelta = new Vector2(110f, 170f);
            Place(actor);
            _npcObjects.Add(actor.root.gameObject);
            _npcs[npc.Id] = actor;
            _npcSpecs[npc.Id] = npc;
        }

        /// <summary>A training dummy (docs 15.12): an enemy standing on the map, an Nro mob or a costume, with a red name; a tap focuses it.</summary>
        void AddDummy(NpcSpec npc)
        {
            var actor = NewActor(npc.Name, npc.MobId >= 0 ? 0 : npc.CostumeId, new Color(1f, 0.45f, 0.35f), 20);
            actor.root.name = "Dummy_" + npc.Id;
            actor.id = 0;
            if (npc.MobId >= 0)
            {
                var mobGo = new GameObject("MobView", typeof(RectTransform), typeof(BbMobView));
                mobGo.transform.SetParent(actor.root, false);
                var mr = (RectTransform)mobGo.transform;
                mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0f);
                mr.pivot = new Vector2(0.5f, 0f);
                mr.anchoredPosition = Vector2.zero;
                mr.sizeDelta = new Vector2(10f, 10f);
                actor.mob = mobGo.GetComponent<BbMobView>();
                actor.mob.uiScale = characterScale;
                actor.mob.Show(npc.MobId);
            }
            actor.facing = -1;                                                   // it looks toward the village entrance, where the player comes from
            actor.x = npc.X;
            actor.y = GroundAt(npc.X);
            float h = DummyHeightOf(actor);
            actor.label.rectTransform.anchoredPosition = new Vector2(0f, h + 26f);  // room for the target arrow between the head and the name
            var tap = actor.root.gameObject.AddComponent<Image>();
            tap.color = new Color(0f, 0f, 0f, 0f);
            var button = actor.root.gameObject.AddComponent<Button>();
            button.targetGraphic = tap;
            string key = npc.Id;
            button.onClick.AddListener(() => { if (DummyTapped != null) DummyTapped(key); });
            actor.root.sizeDelta = new Vector2(110f, Mathf.Max(120f, h + 20f));
            Place(actor);
            _npcObjects.Add(actor.root.gameObject);
            _dummies[npc.Id] = actor;
        }

        static float DummyHeightOf(Actor a)
        {
            if (a.key != null) return a.view.OverheadHeight + (a.boss ? 30f : 16f);          // a monster of a dungeon: above its HP bar
            if (a.mob != null && a.mob.Height > 0f) return a.mob.Height;
            return a.view.OverheadHeight > 0f ? a.view.OverheadHeight * 0.8f : 86f;
        }

        // ---- what the training needs from the village (docs 15.12)

        /// <summary>The key of the dummy the player focuses (null = none): the AI characters walk to both sides of it instead of following the main character.</summary>
        public string CombatTarget { get; set; }

        /// <summary>Canvas pixels per Nro pixel of this map: the server counts in Nro pixels.</summary>
        public float UiScale { get { return _map != null && _map.UiScale > 0f ? _map.UiScale : characterScale; } }
        public float MapWidth { get { return _map != null ? _map.Width : 0f; } }
        public float MapHeight { get { return _map != null ? WorldHeight : 0f; } }
        public RectTransform WorldLayer { get { return _world; } }
        public IEnumerable<string> DummyKeys { get { return _dummies.Keys; } }

        // ---- the main character walks to a target by itself (training: a short-range skill needs the target close; the stick takes the control back)
        float _approachX, _approachStop;
        float _lastFlySound;
        public bool Approaching { get; private set; }
        /// <summary>A training session is running: the AI characters walk to the dummy nearest to each of them in view.</summary>
        public bool AiHunt { get; set; }
        /// <summary>The stick is held (a push of it also ends a walk to a target).</summary>
        public bool StickHeld { get; private set; }

        public void ApproachTo(float x, float stopDistance)
        {
            _approachIsPoint = false;
            _approachX = x;
            _approachStop = Mathf.Max(20f, stopDistance);
            Approaching = true;
        }

        public void CancelApproach()
        {
            Approaching = false;
        }

        /// <summary>The whole view shakes by this many canvas px (the explosion of Tự phát nổ).</summary>
        public Vector2 ShakeOffset { get; set; }

        /// <summary>
        /// The server holds this character (the effect mask of its entity: busy casting, stunned, asleep, held, in the jar): the stick and the walk of the AI do nothing and
        /// it stays where it is, the way the Nro client locks the character while it charges (Char.isLockMove) or is under an effect.
        /// </summary>
        public void SetLocked(ulong characterId, bool locked)
        {
            foreach (var a in _actors) if (a.id == characterId) a.locked = locked;
        }

        /// <summary>The character is still playing the poses of a skill (Nro: skillPaint != null); the next skill waits for them to end.</summary>
        public bool IsActing(ulong characterId)
        {
            foreach (var a in _actors) if (a.id == characterId) return a.view.Acting;
            return false;
        }

        readonly HashSet<ulong> _hidden = new HashSet<ulong>();

        /// <summary>The character is not painted (Nro: Char.isPaint is false while it fuses or splits); the choice survives SetCast.</summary>
        public void SetHidden(ulong characterId, bool hidden)
        {
            if (hidden) _hidden.Add(characterId); else _hidden.Remove(characterId);
            foreach (var a in _actors) if (a.id == characterId && a.root != null) a.root.gameObject.SetActive(!hidden);
        }

        public bool IsLocked(ulong characterId)
        {
            foreach (var a in _actors) if (a.id == characterId) return a.locked;
            return false;
        }

        /// <summary>A character of the party stops the poses it is playing (the skill ended or was broken).</summary>
        public void StopAction(ulong characterId)
        {
            foreach (var a in _actors) if (a.id == characterId) a.view.StopAction();
        }

        /// <summary>
        /// A dummy is drawn somewhere else for a while (sucked into the jar of Ma phong ba: spinning around it, then hidden); {@code at} null puts it back on its feet.
        /// Its place for the game (the arrow, the damage numbers) does not change.
        /// </summary>
        public void SetDummyDraw(string key, bool visible, Vector2? at)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a)) return;
            a.root.gameObject.SetActive(visible);
            if (!visible) return;
            if (at.HasValue) a.root.anchoredPosition = at.Value; else Place(a);
        }

        /// <summary>The costume of the jar of Ma phong ba (catalog entry made by SourceData/tools/add_jar_costume.py from the parts 1218, 1219, 1220 of Nro).</summary>
        public const int JarCostume = 9101;

        /// <summary>A dummy is the jar of Ma phong ba (its mob or costume is replaced by the jar costume of Nro, standing) or itself again.</summary>
        public void SetDummyJar(string key, bool jar)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a) || a.jar == jar) return;
            a.jar = jar;
            if (a.mob != null) a.mob.gameObject.SetActive(!jar);
            a.view.Show(jar ? JarCostume : a.costume);
            if (jar) a.view.SetState(BbCharacterView.PoseState.Stand);
        }

        /// <summary>A character of the party is raised by this many Nro px above the ground (only its picture).</summary>
        public void SetLift(ulong characterId, float nroPx)
        {
            foreach (var a in _actors)
                if (a.id == characterId && !Mathf.Approximately(a.lift, nroPx * UiScale)) { a.lift = nroPx * UiScale; Place(a); }
        }

        /// <summary>A character of the party wears this costume for a while (0 = its own again): the great ape of Biến hình.</summary>
        public void SetLook(ulong characterId, int costumeId)
        {
            foreach (var a in _actors)
            {
                if (a.id != characterId || a.look == costumeId) continue;
                a.look = costumeId;
                a.view.Show(costumeId != 0 ? costumeId : a.costume);
                PlaceLabel(a);
            }
        }

        /// <summary>The main character appears at this x (Dịch chuyển tức thời), standing on the ground there or keeping its height when it flies.</summary>
        public void TeleportMain(float x)
        {
            if (_actors.Count == 0 || _map == null) return;
            var main = _actors[0];
            main.x = Mathf.Clamp(x, 60f, _map.Width - 60f);
            float ground = GroundAt(main.x);
            if (main.grounded || main.y < ground) { main.y = ground; main.vy = 0f; }
            Layout(true);
        }

        /// <summary>The keys of the dummies in view (the part of the screen the panel leaves free), from left to right.</summary>
        public List<string> VisibleDummyKeys()
        {
            var list = new List<KeyValuePair<float, string>>();
            float left = Mathf.Max(0f, _camX) + _inset - 10f, right = Mathf.Max(0f, _camX) + _width - _insetRight + 10f;
            foreach (var kv in _dummies) if (kv.Value.x >= left && kv.Value.x <= right && InViewY(kv.Value) && !kv.Value.dying) list.Add(new KeyValuePair<float, string>(kv.Value.x, kv.Key));
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            var keys = new List<string>();
            foreach (var p in list) keys.Add(p.Value);
            return keys;
        }

        /// <summary>The key is the id of an NPC that can be talked to (a dummy is not).</summary>
        public bool IsNpc(string key) { return key != null && _npcs.ContainsKey(key); }

        public NpcSpec NpcOf(string key)
        {
            NpcSpec spec;
            return key != null && _npcSpecs.TryGetValue(key, out spec) ? spec : null;
        }

        /// <summary>The keys of everything the focus can rest on in view, dummies and NPCs, from left to right.</summary>
        public List<string> VisibleTargetKeys()
        {
            var list = new List<KeyValuePair<float, string>>();
            float left = Mathf.Max(0f, _camX) + _inset - 10f, right = Mathf.Max(0f, _camX) + _width - _insetRight + 10f;
            foreach (var kv in _dummies) if (kv.Value.x >= left && kv.Value.x <= right && InViewY(kv.Value) && !kv.Value.dying) list.Add(new KeyValuePair<float, string>(kv.Value.x, kv.Key));
            foreach (var kv in _npcs) if (kv.Value.x >= left && kv.Value.x <= right) list.Add(new KeyValuePair<float, string>(kv.Value.x, kv.Key));
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            var keys = new List<string>();
            foreach (var p in list) keys.Add(p.Value);
            return keys;
        }

        /// <summary>Where a target (a dummy or an NPC) stands (feet, world canvas pixels) and how tall it is.</summary>
        public bool TargetInfo(string key, out Vector2 feet, out float height)
        {
            if (DummyInfo(key, out feet, out height)) return true;
            Actor a;
            if (key != null && _npcs.TryGetValue(key, out a))
            {
                feet = new Vector2(a.x, a.y);
                height = a.view.OverheadHeight > 0f ? a.view.OverheadHeight : 90f;
                return true;
            }
            return false;
        }

        /// <summary>Where a dummy stands (feet, world canvas pixels) and how tall it is.</summary>
        public bool DummyInfo(string key, out Vector2 feet, out float height)
        {
            Actor a;
            if (key != null && _dummies.TryGetValue(key, out a)) { feet = new Vector2(a.x, a.y); height = DummyHeightOf(a); return true; }
            feet = Vector2.zero;
            height = 0f;
            return false;
        }

        /// <summary>The dummy is stunned for this long: its hurt frames (or the injured pose of its costume) go on.</summary>
        public void StunDummy(string key, float seconds)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a)) return;
            if (a.mob != null) a.mob.Stun(seconds);
            else a.view.PlayAction(new[] { (int)NroRig.NroPose.Hit_Injured }, seconds);
        }

        /// <summary>The dummy was hit: the hurt frames of the mob, or the injured pose of the costume.</summary>
        public void HurtDummy(string key)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a)) return;
            if (a.mob != null) a.mob.Hurt();
            else if (!a.view.Acting) a.view.PlayAction(new[] { (int)NroRig.NroPose.Hit_Injured }, 0.25f);
        }

        /// <summary>The main character first, then the AI: character id, feet (world canvas pixels) and facing.</summary>
        public int PartyCount { get { return _actors.Count; } }

        public bool PartyMember(int index, out ulong characterId, out Vector2 feet, out int facing)
        {
            if (index < 0 || index >= _actors.Count) { characterId = 0; feet = Vector2.zero; facing = 1; return false; }
            var a = _actors[index];
            characterId = a.id;
            feet = new Vector2(a.x, a.y);
            facing = a.facing;
            return true;
        }

        /// <summary>Whether this character of the party is off the ground (the flying poses of a skill are used then).</summary>
        public bool Airborne(ulong characterId)
        {
            foreach (var a in _actors) if (a.id == characterId) return !a.grounded;
            return false;
        }

        /// <summary>A character of the party plays these poses (an attack), turned toward faceX when it is given.</summary>
        public void PlayAction(ulong characterId, int[] poses, float seconds, float faceX = float.NaN)
        {
            foreach (var a in _actors)
            {
                if (a.id != characterId) continue;
                if (!float.IsNaN(faceX) && Mathf.Abs(faceX - a.x) > 2f) { a.facing = faceX > a.x ? 1 : -1; Place(a); }
                a.view.PlayAction(poses, seconds);
            }
        }

        int _auraId;
        List<BattleBall.Proto.AuraLayer> _auraLayers = new List<BattleBall.Proto.AuraLayer>();

        /// <summary>The aura the account wears (0 = none): every character of the party has it (owner decision 2026-10-08).</summary>
        public void SetAura(int auraId, IList<BattleBall.Proto.AuraLayer> layers)
        {
            _auraId = auraId;
            _auraLayers = layers != null ? new List<BattleBall.Proto.AuraLayer>(layers) : new List<BattleBall.Proto.AuraLayer>();
            foreach (var a in _actors) a.view.SetAura(_auraId, _auraLayers);
        }

        /// <summary>How far behind the main character the actor at this place of the party stands: the first AI, then the others each a gap further.</summary>
        static float FollowOffset(int index)
        {
            return index <= 0 ? 0f : FirstFollowGap + (index - 1) * AiFollowGap;
        }

        Actor NewActor(string name, int costumeId, Color labelColor, int labelSize)
        {
            var a = new Actor { name = name };
            a.root = NewRect("Actor_" + name, _world);
            a.root.sizeDelta = new Vector2(10f, 10f);
            a.root.pivot = new Vector2(0.5f, 0f);
            var viewGo = new GameObject("View", typeof(RectTransform), typeof(BbCharacterView));
            viewGo.transform.SetParent(a.root, false);
            var vr = (RectTransform)viewGo.transform;
            vr.anchorMin = vr.anchorMax = new Vector2(0.5f, 0f);
            vr.pivot = new Vector2(0.5f, 0f);
            vr.anchoredPosition = Vector2.zero;
            vr.sizeDelta = new Vector2(10f, 10f);
            a.view = viewGo.GetComponent<BbCharacterView>();
            a.view.uiScale = characterScale;
            a.view.Show(costumeId);
            a.costume = costumeId;
            var labelGo = new GameObject("Name", typeof(RectTransform), typeof(Text), typeof(Outline));
            labelGo.transform.SetParent(a.root, false);
            var lr = (RectTransform)labelGo.transform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.anchoredPosition = new Vector2(0f, 150f);
            lr.sizeDelta = new Vector2(220f, 30f);
            a.label = labelGo.GetComponent<Text>();
            a.label.font = font;
            a.label.text = name;
            a.label.fontSize = labelSize;
            a.label.fontStyle = FontStyle.Bold;
            a.label.alignment = TextAnchor.MiddleCenter;
            a.label.color = labelColor;
            a.label.raycastTarget = false;
            labelGo.GetComponent<Outline>().effectColor = new Color(0.15f, 0.08f, 0.02f, 0.95f);
            labelGo.GetComponent<Outline>().effectDistance = new Vector2(1.5f, -1.5f);
            PlaceLabel(a);
            return a;
        }

        /// <summary>The characters of the player: the first one is the main character (the joystick moves it), the others stand beside it as AI.</summary>
        public void SetCast(IList<ActorSpec> cast)
        {
            if (_map == null) ShowMap(BbMaps.StarterVillage);
            _cast = new List<ActorSpec>(cast);
            // keep the position of the group when the main character changes, and the views of characters that stay
            float groupX = _groupX >= 0f ? _groupX : _map.SpawnX;
            var old = new Dictionary<ulong, Actor>();
            foreach (var a in _actors) old[a.id] = a;
            _actors.Clear();
            for (int i = 0; i < cast.Count; i++)
            {
                var spec = cast[i];
                Actor a;
                if (old.TryGetValue(spec.Id, out a))
                {
                    old.Remove(spec.Id);
                    a.view.Show(a.look != 0 ? a.look : spec.CostumeId);          // the great ape of Biến hình stays an ape when the player takes over another character
                    a.costume = spec.CostumeId;
                    PlaceLabel(a);
                    a.view.auraOnlyWhenStanding = !spec.Main;
                }
                else
                {
                    a = NewActor(spec.Name, spec.CostumeId, spec.Main ? new Color(0.65f, 1f, 0.65f) : new Color(1f, 1f, 1f), spec.Main ? 22 : 18);
                    a.view.SetAura(_auraId, _auraLayers);                          // the aura belongs to the party, not to the NPCs
                    a.view.auraOnlyWhenStanding = !spec.Main;                    // the AI shows it only while it stands; the main character keeps it while it runs
                    a.id = spec.Id;
                    a.x = Mathf.Clamp(groupX - FollowOffset(i), 60f, _map.Width - 60f);
                    a.y = GroundAt(a.x);
                }
                a.label.text = spec.Main ? spec.Name : spec.Name + "\n(AI)";           // two short lines: the followers stand close together
                a.label.color = spec.Main ? new Color(0.65f, 1f, 0.65f) : Color.white;
                a.label.fontSize = spec.Main ? 22 : 16;
                a.label.rectTransform.sizeDelta = new Vector2(spec.Main ? 160f : 90f, spec.Main ? 30f : 44f);
                _actors.Add(a);
            }
            foreach (var gone in old.Values) Kill(gone.root.gameObject);
            foreach (var a in _actors) a.root.gameObject.SetActive(!_hidden.Contains(a.id));
            if (_actors.Count > 0 && cast[0].Main) _actors[0].x = Mathf.Clamp(groupX, 60f, _map.Width - 60f);
            // the main character is drawn last (in front), the AI behind it
            for (int i = _actors.Count - 1; i >= 0; i--) _actors[i].root.SetAsLastSibling();
            Layout(true);
        }

        /// <summary>The name stands above the head of the costume (a tall fusion form lifts it).</summary>
        static void PlaceLabel(Actor a)
        {
            float h = a.view.OverheadHeight > 0f ? a.view.OverheadHeight * 0.8f : 86f;
            a.label.rectTransform.anchoredPosition = new Vector2(0f, h + 4f);
        }

        /// <summary>Draws every character and NPC again from the sprites of the scale now in use (after the player changed it in the settings).</summary>
        public void RefreshArt()
        {
            foreach (var a in _actors) { a.view.Show(a.look != 0 ? a.look : a.costume); PlaceLabel(a); }
            foreach (var go in _npcObjects)
            {
                var view = go.GetComponentInChildren<BbCharacterView>(true);
                var npc = go.name.StartsWith("Npc_") ? _map.Npcs.Find(n => "Npc_" + n.Id == go.name)
                        : go.name.StartsWith("Dummy_") ? _map.Npcs.Find(n => "Dummy_" + n.Id == go.name) : null;
                if (npc == null) continue;
                var mob = go.GetComponentInChildren<BbMobView>(true);
                if (mob != null) { BbMobView.ClearCache(); mob.Show(npc.MobId); }
                else if (view != null) view.Show(npc.CostumeId);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- frame

        /// <summary>The width of the menu panel that covers the left of the screen: the view centres on the free part.</summary>
        public void SetInset(float inset, float insetRight = 0f)
        {
            _inset = inset;
            _insetRight = insetRight;              // a second panel on the right (the menu moves there while a shop is open)
        }

        /// <summary>For the screenshot tool: the main character this far from a training dummy (focused), the AI on both sides of it.</summary>
        public void DemoPlaceMain(string dummyKey, float offset)
        {
            Actor d;
            if (!_dummies.TryGetValue(dummyKey, out d) || _actors.Count == 0) return;
            CombatTarget = dummyKey;
            var main = _actors[0];
            main.x = Mathf.Clamp(d.x + offset, 60f, _map.Width - 60f);
            main.y = GroundAt(main.x);
            main.grounded = true;
            main.flying = false;
            main.facing = d.x > main.x ? 1 : -1;
            for (int i = 1; i < _actors.Count; i++)
            {
                var a = _actors[i];
                a.x = Mathf.Clamp(d.x + (i % 2 == 1 ? -1f : 1f) * (DummyGap + (i - 1) / 2 * 40f), 60f, _map.Width - 60f);
                a.y = GroundAt(a.x);
                a.grounded = true;
                a.flying = false;
                a.facing = d.x > a.x ? 1 : -1;
            }
            _groupX = main.x;
            Layout(true);
        }

        /// <summary>Puts the followers right behind the main character at once (the screenshot tool; in play they walk there).</summary>
        public void ResetFormation()
        {
            if (_actors.Count == 0) return;
            var main = _actors[0];
            for (int i = 1; i < _actors.Count; i++)
            {
                _actors[i].x = Mathf.Clamp(main.x - main.facing * FollowOffset(i), 60f, _map.Width - 60f);
                _actors[i].y = GroundAt(_actors[i].x);
                _actors[i].grounded = true;
                _actors[i].flying = false;
            }
            Layout(true);
        }

        void Update()
        {
            Tick((joystick != null ? joystick.Value : Vector2.zero) + DebugStick, Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        }

        /// <summary>For the scripted run in Play mode: a push of the stick.</summary>
        public Vector2 DebugStick;

        /// <summary>One step of the village: the joystick direction moves the main character, the followers walk after it, the view scrolls. Public so a test can drive it.</summary>
        public void Tick(Vector2 input, float dt)
        {
            if (_map == null || _actors.Count == 0) return;
            ReadSize();
            if (_map.Flying) { TickArena(input, dt); return; }
            _clock += dt;
            var main = _actors[0];
            StickHeld = input.sqrMagnitude > 0.04f;
            bool mainLocked = main.locked;
            if (mainLocked) { input = Vector2.zero; Approaching = false; StickHeld = false; }
            if (Approaching)
            {
                float toGo = _approachX - main.x;
                if (StickHeld) Approaching = false;
                else if (Mathf.Abs(toGo) <= _approachStop) { Approaching = false; main.facing = toGo >= 0f ? 1 : -1; }
                else input = new Vector2(Mathf.Sign(toGo), 0f);
            }

            // up jumps; held up in the air it turns into flying: the stick then sets the height (up rises, down sinks, centre hovers) and the pace is faster
            bool wasGrounded = main.grounded;
            if (input.y > 0.6f && main.grounded) { main.vy = Jump; main.grounded = false; main.holdUp = 0f; BbSfx.Play(BbSfx.Move, 0.2f); }
            if (!main.grounded && input.y > 0.6f) main.holdUp += dt; else if (!main.flying) main.holdUp = 0f;
            if (!main.grounded && !main.flying && main.holdUp >= FlyAfter) main.flying = true;
            float vx = input.x * Speed * (main.flying ? FlyBoost : 1f);
            if (!MoveX(main, vx * dt)) vx = 0f;                          // a wall stops the walk (the player flies over it)
            if (Mathf.Abs(input.x) > 0.05f) main.facing = input.x > 0f ? 1 : -1;
            float g = GroundAt(main.x);
            // flying: the stick sets the height; with the stick let go the character sinks by itself and lands
            if (main.flying) Fly(main, mainLocked ? 0f : input.sqrMagnitude < 0.0001f ? -SinkSpeed : Mathf.Abs(input.y) < 0.2f ? 0f : input.y * FlySpeed * (input.y < 0f ? DescentBoost : 1f), dt, g);
            else Walk(main, dt, g);
            // the sounds of the main character's movement (Nro: SoundMn.charJump, charFall, charFly; there are no footsteps in Nro): the landing, the flight every 20 ticks
            if (!wasGrounded && main.grounded) BbSfx.Play(BbSfx.Move, 0.1f);
            if (main.flying && Time.unscaledTime - _lastFlySound > 0.67f) { _lastFlySound = Time.unscaledTime; BbSfx.Play(BbSfx.Fly, 0.2f); }
            bool mainRuns = Mathf.Abs(vx) > 5f;
            main.view.animSpeed = mainRuns ? MainRunAnimSpeed : MainAnimSpeed;
            main.view.SetState(AirState(main) ?? (mainRuns ? BbCharacterView.PoseState.Run : BbCharacterView.PoseState.Stand));

            // while a training session runs every AI character walks to the dummy nearest to it among those in view of the player (not only the one the controlled
            // character focuses: owner request 2026-10-09) and stands beside it, close enough for its punches (docs 15.12)
            var inView = AiHunt ? VisibleDummyKeys() : null;
            for (int i = 1; i < _actors.Count; i++)
            {
                var a = _actors[i];
                if (a.locked)
                {
                    // held by the server (a gong, a charge, a beam, an effect on it): it stands where it is, in the air too, and comes down only if it was falling
                    a.running = false;
                    a.view.animSpeed = 1f;
                    if (!a.flying && !a.climbing) Walk(a, dt, GroundAt(a.x));
                    a.view.SetState(AirState(a) ?? BbCharacterView.PoseState.Stand);
                    continue;
                }
                Actor foe = null;
                if (inView != null)
                {
                    float best = float.MaxValue;
                    foreach (string key in inView)
                    {
                        Actor candidate;
                        if (!_dummies.TryGetValue(key, out candidate)) continue;
                        float far = Mathf.Abs(candidate.x - a.x);
                        if (far < best) { best = far; foe = candidate; }
                    }
                }
                float target = foe != null ? Mathf.Clamp(foe.x + (a.x < foe.x ? -1f : 1f) * (DummyGap + (i - 1) * 40f), 60f, _map.Width - 60f)
                                           : Mathf.Clamp(main.x - main.facing * FollowOffset(i), 60f, _map.Width - 60f);
                float d = target - a.x;
                // start walking when 24 px behind its place, stop within 6 px: a character that keeps up with a running main one used to flip between
                // running and standing every few frames, and every flip restarts the run frames (that looked like very fast frames)
                if (!a.running && Mathf.Abs(d) > 24f) { a.running = true; a.runUntil = _clock + MinRunTime; }
                else if (a.running && Mathf.Abs(d) < 6f && _clock >= a.runUntil) a.running = false;
                bool moving = a.running;
                if (moving)
                {
                    float step = Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), Speed * 1.15f * dt);
                    a.facing = d > 0f ? 1 : -1;
                    if (!MoveX(a, step) && !a.climbing && !main.flying) a.climbing = true;   // a wall in the way: the AI flies up it
                }
                else a.facing = foe != null ? (foe.x > a.x ? 1 : -1) : main.facing;

                float ga = GroundAt(a.x);
                float targetY = Mathf.Max(ga, main.y - 24f * i);
                if (main.flying && foe == null && (targetY > ga + 0.5f || a.y > ga + 0.5f))
                {
                    // the AI flies with the main character, a little lower; once it has come down to the ground it stands there, whatever the main one does
                    a.climbing = false;
                    a.flying = true;
                    a.grounded = false;
                    float before = a.y;
                    a.y = Mathf.MoveTowards(a.y, targetY, (targetY < a.y ? SinkSpeed : FlySpeed) * 1.2f * dt);
                    a.vy = (a.y - before) / Mathf.Max(0.0001f, dt);
                    if (a.y <= ga + 0.01f && targetY <= ga + 0.5f) { a.y = ga; a.vy = 0f; a.grounded = true; a.flying = false; }
                }
                else if (a.climbing)
                {
                    // up the wall at flying speed, then over its top (the walk goes on and gravity brings it down onto the ledge)
                    a.flying = true;
                    a.grounded = false;
                    a.y += FlySpeed * 1.4f * dt;
                    a.vy = FlySpeed * 1.4f;
                    float ahead = GroundAt(a.x + a.facing * 40f);
                    if (a.y >= Mathf.Max(ga, ahead) + 10f) { a.climbing = false; a.flying = false; a.vy = 0f; }
                }
                else
                {
                    if (a.flying) { a.flying = false; a.vy = 0f; }
                    if (!main.grounded && a.grounded && main.vy > 0f && Mathf.Abs(d) < 120f) { a.vy = Jump * 0.9f; a.grounded = false; }
                    Walk(a, dt, ga);
                }
                // the run frames of the AI: a fixed pace while it runs
                a.view.animSpeed = moving ? AiRunAnimSpeed : 1f;
                a.view.SetState(AirState(a) ?? (moving ? BbCharacterView.PoseState.Run : BbCharacterView.PoseState.Stand));
            }
            _groupX = main.x;
            Layout(false, dt);
        }

        /// <summary>
        /// Moves along x unless the ground ahead rises too much (more than a step when walking, more than the feet's height when in the air).
        /// Returns false when a wall stopped the move.
        /// </summary>
        bool MoveX(Actor a, float dx)
        {
            if (Mathf.Abs(dx) < 0.0001f) return true;
            float nx = Mathf.Clamp(a.x + dx, 60f, _map.Width - 60f);
            float gn = GroundAt(nx);
            bool blocked = a.grounded ? gn > a.y + MaxStep : gn > a.y + 4f;
            if (blocked) return false;
            a.x = nx;
            return true;
        }

        /// <summary>The pose of a character that is off the ground, or null on the ground: coming down (sinking, stick pushed down, falling after a jump) is Fall_Down.</summary>
        static BbCharacterView.PoseState? AirState(Actor a)
        {
            if (a.grounded) return null;
            if (a.vy < -1f) return BbCharacterView.PoseState.Fall;
            return a.flying ? BbCharacterView.PoseState.Fly : BbCharacterView.PoseState.Jump;
        }

        /// <summary>On the ground the feet follow the terrain (steps up to MaxStep); a bigger drop under the feet is a ledge: the character falls. In the air gravity works.</summary>
        static void Walk(Actor a, float dt, float g)
        {
            if (a.grounded)
            {
                if (g >= a.y - MaxStep) a.y = g;
                else a.grounded = false;
            }
            if (!a.grounded) Fall(a, dt, g);
        }

        static void Fall(Actor a, float dt, float g)
        {
            a.vy -= Gravity * dt;
            a.y += a.vy * dt;
            if (a.y <= g && a.vy <= 0f) { a.y = g; a.vy = 0f; a.grounded = true; a.flying = false; a.holdUp = 0f; }
        }

        /// <summary>Flying: the vertical speed is whatever the stick says, between the ground and the top of the world; reaching the ground ends the flight.</summary>
        void Fly(Actor a, float vy, float dt, float g)
        {
            a.vy = vy;
            a.y = Mathf.Clamp(a.y + vy * dt, g, Ceiling);
            if (a.y <= g) { a.y = g; a.vy = 0f; a.grounded = true; a.flying = false; a.holdUp = 0f; }
        }

        /// <summary>Puts the actors where they are and scrolls the view so that the main character is in the middle of the part the menu leaves free.</summary>
        void Layout(bool snap, float dt = 0.016f)
        {
            if (_map == null) return;
            ReadSize();
            foreach (var a in _actors) Place(a);
            foreach (var a in _dummies.Values) Place(a);
            if (_actors.Count > 0)
            {
                float centre = _inset + (_width - _inset - _insetRight) * 0.5f;
                float target = Mathf.Clamp(_actors[0].x - centre, 0f, Mathf.Max(0f, _map.Width - _width));
                _camX = snap || _camX < 0f ? target : Mathf.Lerp(_camX, target, 1f - Mathf.Exp(-8f * dt));
                float targetY = Mathf.Clamp(_actors[0].y - _height * (_map.Flying ? 0.45f : 0.38f), 0f, Mathf.Max(0f, WorldHeight - _height));
                _camY = snap ? targetY : Mathf.Lerp(_camY, targetY, 1f - Mathf.Exp(-6f * dt));
            }
            else { _camX = 0f; _camY = 0f; }
            for (int i = 0; i < _layers.Count; i++)
                _layers[i].anchoredPosition = new Vector2(-_camX * _layerParallax[i], -_camY * _layerParallax[i]) + ShakeOffset;
        }

        void Place(Actor a)
        {
            a.root.anchoredPosition = new Vector2(a.x, a.y + a.lift);
            a.view.transform.localScale = new Vector3(a.facing, 1f, 1f);
        }

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);       // the screenshot tool builds the view outside play mode
        }

        // ---------------------------------------------------------------------------------------------------------------- building blocks

        RectTransform NewLayer(string name, Transform parent)
        {
            var r = NewRect(name, parent);
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            r.sizeDelta = new Vector2(_map.Width, WorldHeight);
            r.anchoredPosition = Vector2.zero;
            return r;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            return r;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static Image NewImage(RectTransform parent, string name, Sprite sprite, Color color, float x, float y, float w, float h, Vector2 pivot)
        {
            var r = NewRect(name, parent);
            r.pivot = pivot;
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Sprite Gradient(Color top, Color bottom)
        {
            const int n = 64;
            var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++) tex.SetPixel(0, i, Color.Lerp(bottom, top, i / (float)(n - 1)));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
