using System.Collections.Generic;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The village view as the arena of a dungeon (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25): the same world layer, camera, party and name labels, but the map
    /// is a sky with no ground, the characters fly in every direction, the AI characters and the monsters are where the server says they are, and a monster is an actor made of
    /// Nro parts with an HP bar as wide as its body. The server counts in Nro pixels with y upward, like this view (canvas px = Nro px x UiScale).
    /// </summary>
    public sealed partial class BbVillageView
    {
        public Sprite flareSprite;                // myTexture2dflare.png of Nro: the glow over a dead character
        public Sprite[] arenaBackdrops;           // the space pictures behind a dungeon, one for each dungeon in turn (docs 15.34); empty = the old sky with clouds
        public Sprite[] arenaObjects;             // asteroids, planets and black holes that drift in front of the backdrop (see ObjectKinds)
        int _arenaStyle;                          // which backdrop: the number of the dungeon
        readonly List<Drifter> _drifters = new List<Drifter>();

        /// <summary>What an object picture is: 0 asteroid (tumbles), 1 planet (turns very slowly), 2 black hole (only drifts). In the order tools/make_arena_backdrops.py numbers them.</summary>
        static readonly int[] ObjectKinds = { 2, 0, 2, 0, 2, 0, 1, 1, 2, 2 };

        sealed class Drifter
        {
            public RectTransform rect;
            public Vector2 position, velocity;
            public float angle, spin, spanX, spanY, margin;
        }

        bool HasSpaceBackdrop { get { return _map != null && _map.Flying && arenaBackdrops != null && arenaBackdrops.Length > 0 && arenaBackdrops[Mathf.Abs(_arenaStyle) % arenaBackdrops.Length] != null; } }

        List<ActorSpec> _cast = new List<ActorSpec>();
        BbMapChunk _villageMap;
        string _villageMapId;
        float _villageGroupX = -1f;
        Vector2 _approachPoint;
        bool _approachIsPoint;
        string[] _depthOrder = new string[0];

        /// <summary>The view shows the arena of a dungeon instead of the village.</summary>
        public bool InArena { get; private set; }

        const float ArenaSpeed = 700f, ArenaMargin = 40f, ArenaFeetMin = 20f, ArenaHeadroom = 120f;
        const float DeathThrow = 260f, DeathDrift = 230f, DeathGravity = 1700f;

        /// <summary>The sky of a dungeon of this tier (docs 15.25): day for the early ones, dusk for the middle ones, night for the late ones; the clouds take its tint.</summary>
        public static BbMapChunk MakeArena(float arenaW, float arenaH, float uiScale, string tier)
        {
            var map = new BbMapChunk { Id = "arena", Name = "Phó bản", IsPlaceholder = true, Flying = true, UiScale = uiScale, Width = arenaW * uiScale, WorldHeight = arenaH * uiScale, GroundHeight = 0f };
            map.SpawnX = 200f * uiScale;
            switch (tier)
            {
                case "mid":
                    map.SkyTop = new Color(0.86f, 0.40f, 0.30f); map.SkyBottom = new Color(1f, 0.82f, 0.52f); break;
                case "late":
                    map.SkyTop = new Color(0.13f, 0.08f, 0.30f); map.SkyBottom = new Color(0.52f, 0.28f, 0.52f); break;
                default:
                    map.SkyTop = new Color(0.28f, 0.56f, 0.92f); map.SkyBottom = new Color(0.80f, 0.92f, 1f); break;
            }
            return map;
        }

        /// <summary>The map is replaced by the arena: the party is made again in it (the server places it), every NPC of the village is gone until <see cref="LeaveArena"/>.</summary>
        public void EnterArena(float arenaW, float arenaH, string tier, int style = 0)
        {
            if (InArena) return;
            _arenaStyle = style;
            _villageMap = _map;
            _villageMapId = _mapId;
            _villageGroupX = _groupX;
            _map = MakeArena(arenaW, arenaH, characterScale, tier);
            _mapId = "arena";
            InArena = true;
            _groupX = -1f;
            Approaching = false;
            Build();
            SetCast(_cast);
            if (Weather != null) Weather.SetVisible(false);
        }

        /// <summary>Back to the village where the player left it.</summary>
        public void LeaveArena()
        {
            if (!InArena) return;
            InArena = false;
            _map = _villageMap;
            _mapId = _villageMapId;
            _groupX = _villageGroupX;
            Approaching = false;
            _depthOrder = new string[0];
            Build();
            SetCast(_cast);
            if (Weather != null) Weather.SetVisible(true);
        }

        // ---------------------------------------------------------------------------------------------------------------- the backdrop

        /// <summary>Clouds on three layers that move slower than the arena (far, middle, near) and a mist; all made of the soft circle sprite.</summary>
        void BuildArenaBackdrop()
        {
            if (HasSpaceBackdrop) { BuildSpaceBackdrop(); return; }
            var tint = _map.SkyTop.r > 0.7f ? new Color(1f, 0.92f, 0.82f) : _map.SkyTop.b > 0.25f && _map.SkyTop.r < 0.2f ? new Color(0.80f, 0.70f, 0.95f) : Color.white;
            var rng = new System.Random(11);
            float[] parallax = { 0.12f, 0.28f, 0.55f };
            float[] alpha = { 0.22f, 0.42f, 0.70f };
            float[] scale = { 2.4f, 1.5f, 1f };
            for (int l = 0; l < parallax.Length; l++)
            {
                var layer = NewLayer("Clouds" + l, viewport);
                _layers.Add(layer);
                _layerParallax.Add(parallax[l]);
                float spanX = _map.Width * parallax[l] + _width + 400f, spanY = WorldHeight * parallax[l] + _height + 200f;
                int count = Mathf.CeilToInt(spanX / (360f * scale[l])) * 2;
                for (int i = 0; i < count; i++)
                {
                    float cx = (float)rng.NextDouble() * spanX, cy = (float)rng.NextDouble() * spanY;
                    float w = (220f + (float)rng.NextDouble() * 220f) * scale[l], h = w * (0.22f + (float)rng.NextDouble() * 0.10f);
                    var c = new Color(tint.r, tint.g, tint.b, alpha[l]);
                    NewImage(layer, "Cloud", dotSprite, c, cx, cy, w, h, new Vector2(0.5f, 0.5f));
                    NewImage(layer, "Cloud", dotSprite, c, cx + w * 0.30f, cy + h * 0.25f, w * 0.60f, h * 0.9f, new Vector2(0.5f, 0.5f));
                    NewImage(layer, "Cloud", dotSprite, c, cx - w * 0.28f, cy + h * 0.1f, w * 0.55f, h * 0.8f, new Vector2(0.5f, 0.5f));
                }
            }
        }

        /// <summary>
        /// Space behind a dungeon (owner request 2026-10-12, docs 15.34): a picture that fills the screen and moves slowly against the camera, and two layers of asteroids, planets
        /// and black holes that drift and turn in front of it, the nearer layer smaller and faster. Every dungeon has its own picture and its own objects.
        /// </summary>
        void BuildSpaceBackdrop()
        {
            var bg = arenaBackdrops[Mathf.Abs(_arenaStyle) % arenaBackdrops.Length];
            const float farP = 0.22f;
            var far = NewLayer("SpaceBg", viewport);
            _layers.Add(far);
            _layerParallax.Add(farP);
            float needW = Mathf.Max(_width, (_map.Width - _width) * farP + _width), needH = Mathf.Max(_height, (WorldHeight - _height) * farP + _height);   // what the camera can reach of this layer
            float aspect = bg.rect.width / bg.rect.height;
            float w = Mathf.Max(needW, needH * aspect) + 4f, h = w / aspect;
            NewImage(far, "Space", bg, Color.white, (needW - w) * 0.5f, (needH - h) * 0.5f, w, h, Vector2.zero);

            if (arenaObjects == null || arenaObjects.Length == 0) return;
            var rng = new System.Random(77 + _arenaStyle * 13);
            AddDriftLayer("SpaceFar", 0.40f, 5, 0.30f, 0.55f, 0.55f, 0.85f, 5f, rng);
            AddDriftLayer("SpaceNear", 0.70f, 3, 0.12f, 0.24f, 0.78f, 1f, 11f, rng);
        }

        /// <summary>One layer of drifting objects (parallax p): sizes are shares of the screen height, speed is the drift in canvas px per second.</summary>
        void AddDriftLayer(string name, float p, int count, float sizeMin, float sizeMax, float shade, float alpha, float speed, System.Random rng)
        {
            var layer = NewLayer(name, viewport);
            _layers.Add(layer);
            _layerParallax.Add(p);
            float spanX = Mathf.Max(_width, (_map.Width - _width) * p + _width), spanY = Mathf.Max(_height, (WorldHeight - _height) * p + _height);
            for (int i = 0; i < count; i++)
            {
                int pick = rng.Next(arenaObjects.Length);
                var sprite = arenaObjects[pick];
                if (sprite == null) continue;
                int kind = pick < ObjectKinds.Length ? ObjectKinds[pick] : 0;
                float size = (sizeMin + (float)rng.NextDouble() * (sizeMax - sizeMin)) * _height * (kind == 0 ? 0.7f : 1f);          // the rocks are smaller than the planets
                float sw = sprite.rect.width, sh = sprite.rect.height;
                float scale = size / Mathf.Max(sw, sh);
                var img = NewImage(layer, "Drift", sprite, new Color(shade, shade, shade, alpha), 0f, 0f, sw * scale, sh * scale, new Vector2(0.5f, 0.5f));
                img.raycastTarget = false;
                float angle = (float)rng.NextDouble() * 360f;
                float heading = (float)rng.NextDouble() * Mathf.PI * 2f;
                float v = speed * (0.5f + (float)rng.NextDouble());
                _drifters.Add(new Drifter
                {
                    rect = img.rectTransform,
                    position = new Vector2((float)rng.NextDouble() * spanX, (float)rng.NextDouble() * spanY),
                    velocity = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * v,
                    angle = angle,
                    spin = kind == 0 ? (rng.Next(2) == 0 ? -1f : 1f) * (4f + (float)rng.NextDouble() * 8f) : kind == 1 ? 0.8f : 0f,
                    spanX = spanX,
                    spanY = spanY,
                    margin = Mathf.Max(sw, sh) * scale * 0.5f
                });
            }
        }

        /// <summary>The drifting objects move on and turn; one that leaves the layer comes back in from the other side.</summary>
        void DriftArenaObjects(float dt)
        {
            foreach (var d in _drifters)
            {
                if (d.rect == null) continue;
                d.position += d.velocity * dt;
                d.angle += d.spin * dt;
                float spanX = d.spanX + 2f * d.margin, spanY = d.spanY + 2f * d.margin;
                float x = Mathf.Repeat(d.position.x + d.margin, spanX) - d.margin;
                float y = Mathf.Repeat(d.position.y + d.margin, spanY) - d.margin;
                d.rect.anchoredPosition = new Vector2(x, y);
                d.rect.localEulerAngles = new Vector3(0f, 0f, d.angle);
            }
        }

        /// <summary>A sea of cloud along the bottom of the arena, in front of the far layers and behind everyone: the characters never go below it.</summary>
        void BuildArenaFloor()
        {
            var tint = _map.SkyTop.r > 0.7f ? new Color(1f, 0.90f, 0.78f) : _map.SkyTop.r < 0.2f ? new Color(0.62f, 0.45f, 0.75f) : Color.white;
            var floor = NewImage(_world, "Floor", Gradient(new Color(tint.r, tint.g, tint.b, 0.9f), new Color(tint.r, tint.g, tint.b, 0f)), Color.white, 0f, 0f, _map.Width, 150f, Vector2.zero);
            floor.rectTransform.localScale = new Vector3(1f, -1f, 1f);                        // opaque at the very bottom, fading upward
            floor.rectTransform.pivot = new Vector2(0f, 1f);
            floor.rectTransform.anchoredPosition = new Vector2(0f, 150f);
            floor.transform.SetAsFirstSibling();
        }

        // ---------------------------------------------------------------------------------------------------------------- the party in the arena

        /// <summary>Where the server puts a character of the party (the main character too, at the start and after a teleport): the picture is there at once.</summary>
        public void SnapParty(ulong characterId, Vector2 at)
        {
            foreach (var a in _actors)
                if (a.id == characterId) { a.x = a.tx = at.x; a.y = a.ty = at.y; Place(a); }
        }

        /// <summary>An AI character is where the server says it is: the picture glides there.</summary>
        public void SetRemote(ulong characterId, Vector2 at, float dirX)
        {
            for (int i = 1; i < _actors.Count; i++)
            {
                var a = _actors[i];
                if (a.id != characterId) continue;
                a.remote = true;
                a.tx = at.x;
                a.ty = at.y;
                if (Mathf.Abs(dirX) > 0.05f) a.facing = dirX > 0f ? 1 : -1;
            }
        }

        /// <summary>The main character flies to this point by itself (a skill needs the target close); the stick takes the control back.</summary>
        public void ApproachToPoint(Vector2 point, float stopDistance)
        {
            if (!InArena) { ApproachTo(point.x, stopDistance); return; }
            _approachPoint = point;
            _approachIsPoint = true;
            _approachStop = Mathf.Max(20f, stopDistance);
            Approaching = true;
        }

        /// <summary>The main character appears at this point (Dịch chuyển tức thời).</summary>
        public void TeleportMainTo(Vector2 at)
        {
            if (_actors.Count == 0 || _map == null) return;
            var main = _actors[0];
            main.x = main.tx = Mathf.Clamp(at.x, ArenaMargin, _map.Width - ArenaMargin);
            main.y = main.ty = Mathf.Clamp(at.y, ArenaFeetMin, WorldHeight - ArenaHeadroom);
            Layout(true);
        }

        /// <summary>The part of the arena the screen shows, in Nro pixels (x and y of the corner with the smallest values, then the size): the AI fight what is inside it.</summary>
        public bool ViewRect(out Rect rect)
        {
            rect = new Rect();
            if (!InArena || _map == null) return false;
            float s = UiScale;
            float left = Mathf.Max(0f, _camX) + _inset, right = Mathf.Max(0f, _camX) + _width - _insetRight;
            rect = new Rect(left / s, _camY / s, Mathf.Max(1f, right - left) / s, _height / s);
            return true;
        }

        bool InViewY(Actor a)
        {
            if (!InArena) return true;
            return a.y >= _camY - 30f && a.y <= _camY + _height + 30f;
        }

        // ---------------------------------------------------------------------------------------------------------------- enemies

        /// <summary>A monster of the dungeon: Nro parts drawn like a costume, with an HP bar over its head as wide as its body (a boss also shows its name).</summary>
        public void AddEnemy(string key, string name, int head, int body, int leg, bool boss, Vector2 at)
        {
            if (!InArena || key == null || _dummies.ContainsKey(key)) return;
            var a = NewActor(name, 0, boss ? new Color(1f, 0.35f, 0.30f) : new Color(1f, 0.62f, 0.5f), boss ? 24 : 14);
            a.root.name = "Enemy_" + key;
            a.key = key;
            a.id = 0;
            a.boss = boss;
            a.remote = true;
            a.view.ShowParts(head, body, leg);
            a.view.animSpeed = 1.2f;
            a.view.SetState(BbCharacterView.PoseState.Fly);
            a.facing = -1;
            a.x = a.tx = at.x;
            a.y = a.ty = at.y;
            float w = Mathf.Max(boss ? 90f : 40f, a.view.FigureWidth);
            float h = a.view.OverheadHeight;
            float bar = boss ? 10f : 6f;
            a.hpBack = EnemyBar(a.root, "HpBack", new Color(0.05f, 0.08f, 0.05f, 0.9f), w, bar, h + 4f);
            a.hpFill = EnemyBar(a.root, "HpFill", new Color(0.30f, 0.88f, 0.28f), w - 2f, bar - 2f, h + 5f);
            a.label.gameObject.SetActive(boss);
            a.label.rectTransform.anchoredPosition = new Vector2(0f, h + 6f + bar);
            var tap = a.root.gameObject.AddComponent<Image>();
            tap.color = new Color(0f, 0f, 0f, 0f);
            var button = a.root.gameObject.AddComponent<Button>();
            button.targetGraphic = tap;
            string k = key;
            button.onClick.AddListener(() => { if (DummyTapped != null) DummyTapped(k); });
            a.root.sizeDelta = new Vector2(Mathf.Max(70f, w + 20f), Mathf.Max(70f, h + 20f));
            _dummies[key] = a;
            Place(a);
        }

        Image EnemyBar(RectTransform parent, string name, Color color, float w, float h, float y)
        {
            var r = NewRect(name, parent);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0f, 0f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(-w * 0.5f, y);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = whiteSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A monster is where the server says it is and has this much HP left (0 to 1).</summary>
        public void SetEnemy(string key, Vector2 at, float dirX, float hpRatio)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a) || a.dying) return;
            a.tx = at.x;
            a.ty = at.y;
            if (Mathf.Abs(dirX) > 0.05f) a.facing = dirX > 0f ? 1 : -1;
            a.hpRatio = Mathf.Clamp01(hpRatio);
        }

        /// <summary>A monster plays the poses of an attack, turned toward faceX.</summary>
        public void EnemyAction(string key, int[] poses, float seconds, float faceX = float.NaN)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a) || a.dying) return;
            if (!float.IsNaN(faceX) && Mathf.Abs(faceX - a.x) > 2f) { a.facing = faceX > a.x ? 1 : -1; Place(a); }
            a.view.PlayAction(poses, seconds);
        }

        /// <summary>A monster fell (Nro Mob.startDie): it is thrown away from the blow, tumbles, blinks and is gone.</summary>
        public void KillEnemy(string key, Vector2 away)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a) || a.dying) return;
            a.dying = true;
            a.dyingAge = 0f;
            float side = Mathf.Abs(away.x) > 0.05f ? Mathf.Sign(away.x) : (a.facing > 0 ? -1f : 1f);
            a.dyingVelocity = new Vector2(side * DeathDrift * (a.boss ? 0.5f : 1f), DeathThrow);
            if (a.hpBack != null) a.hpBack.gameObject.SetActive(false);
            if (a.hpFill != null) a.hpFill.gameObject.SetActive(false);
            a.label.gameObject.SetActive(false);
            a.view.PlayAction(new[] { (int)NroPose.Hit_Injured }, 5f);
        }

        /// <summary>An evolving boss turned into its next form (docs 15.30): the same actor with the parts and the name of the new one.</summary>
        public void ReskinEnemy(string key, string name, int head, int body, int leg)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a) || a.dying) return;
            a.view.ShowParts(head, body, leg);
            a.name = name;
            a.label.text = name;
            a.chargeUntil = 0f;
            if (a.chargeGlow != null) a.chargeGlow.gameObject.SetActive(false);
        }

        /// <summary>A boss charges for this many seconds (the self-destruct pose of the Xayda without the blast): a white glow pulses over it.</summary>
        public void SetEnemyCharge(string key, float seconds)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a) || a.dying) return;
            a.chargeUntil = seconds > 0f ? Time.unscaledTime + seconds : 0f;
            if (a.chargeGlow == null && seconds > 0f)
            {
                float w = Mathf.Max(90f, a.view.FigureWidth) + 50f, h = Mathf.Max(90f, a.view.OverheadHeight) + 20f;
                var glow = NewImage(a.root, "ChargeGlow", dotSprite, new Color(1f, 1f, 1f, 0f), 0f, 0f, w, h, new Vector2(0.5f, 0f));
                glow.raycastTarget = false;
                glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                glow.rectTransform.anchoredPosition = Vector2.zero;
                a.chargeGlow = glow;
            }
        }

        /// <summary>A monster is gone without a fall (the server despawned it).</summary>
        public void RemoveEnemy(string key)
        {
            Actor a;
            if (key == null || !_dummies.TryGetValue(key, out a)) return;
            if (a.dying) return;                                       // it is falling: DyingStep removes it
            Kill(a.root.gameObject);
            _dummies.Remove(key);
        }

        /// <summary>
        /// The player takes over another character of the party in the arena (owner request 2026-10-11): it becomes the main one (the stick moves it, the screen follows it) and the
        /// one that was main becomes an AI where it stands. Nothing else of either changes (the ape form, a pose in progress, a ghost).
        /// </summary>
        public bool SwitchMain(ulong characterId)
        {
            int i = _actors.FindIndex(a => a.id == characterId);
            if (i < 0) return false;
            if (i == 0) return true;
            var old = _actors[0];
            var main = _actors[i];
            _actors.RemoveAt(i);
            _actors.Insert(0, main);
            main.remote = false;
            main.tx = main.x;
            main.ty = main.y;
            old.remote = true;
            old.tx = old.x;
            old.ty = old.y;
            StyleLabel(main, true);
            StyleLabel(old, false);
            main.view.auraOnlyWhenStanding = false;
            old.view.auraOnlyWhenStanding = true;
            Approaching = false;
            var cast = _cast.FindIndex(c => c.Id == characterId);
            if (cast > 0) { var spec = _cast[cast]; _cast.RemoveAt(cast); _cast.Insert(0, spec); }
            for (int k = 0; k < _cast.Count; k++) { var c = _cast[k]; c.Main = k == 0; _cast[k] = c; }
            return true;
        }

        static void StyleLabel(Actor a, bool main)
        {
            a.label.text = main ? a.name : a.name + "\n(AI)";
            a.label.color = main ? new Color(0.65f, 1f, 0.65f) : Color.white;
            a.label.fontSize = main ? 22 : 16;
            a.label.rectTransform.sizeDelta = new Vector2(main ? 160f : 90f, main ? 30f : 44f);
        }

        /// <summary>A character of the party turns to face right (1) or left (-1), for example to follow its own beam.</summary>
        public void FaceParty(ulong characterId, int facing)
        {
            foreach (var a in _actors)
                if (a.id == characterId && a.facing != facing) { a.facing = facing; Place(a); return; }
        }

        /// <summary>For the play scenario: the costume a character of the party wears now instead of its own (0 = its own).</summary>
        public int LookOf(ulong characterId)
        {
            foreach (var a in _actors) if (a.id == characterId) return a.look;
            return -1;
        }

        public bool HasEnemy(string key) { return key != null && _dummies.ContainsKey(key); }

        /// <summary>For the play scenario: how many monsters stand in the arena (the falling ones too) and how many of them are on the screen.</summary>
        public int EnemyCount { get { return _dummies.Count; } }
        public int EnemiesOnScreen { get { return VisibleDummyKeys().Count; } }
        public string DebugCamera { get { return "cam " + Mathf.RoundToInt(_camX) + "," + Mathf.RoundToInt(_camY) + " screen " + Mathf.RoundToInt(_width) + "x" + Mathf.RoundToInt(_height) + " world " + Mathf.RoundToInt(_map != null ? _map.Width : 0f) + "x" + Mathf.RoundToInt(WorldHeight); } }

        // ---------------------------------------------------------------------------------------------------------------- the frame

        void TickArena(Vector2 input, float dt)
        {
            _clock += dt;
            var main = _actors[0];
            StickHeld = input.sqrMagnitude > 0.04f;
            if (main.locked || main.ghost || main.dying) { input = Vector2.zero; Approaching = false; StickHeld = false; }
            if (Approaching)
            {
                Vector2 to = (_approachIsPoint ? _approachPoint : new Vector2(_approachX, main.y)) - new Vector2(main.x, main.y);
                if (StickHeld) Approaching = false;
                else if (to.magnitude <= _approachStop) { Approaching = false; if (Mathf.Abs(to.x) > 1f) main.facing = to.x >= 0f ? 1 : -1; }
                else input = to.normalized;
            }
            Vector2 v = input.sqrMagnitude > 1f ? input.normalized : input;
            if (!main.dying && !main.ghost)
            {
                main.x = Mathf.Clamp(main.x + v.x * ArenaSpeed * dt, ArenaMargin, _map.Width - ArenaMargin);
                main.y = Mathf.Clamp(main.y + v.y * ArenaSpeed * dt, ArenaFeetMin, WorldHeight - ArenaHeadroom);
                if (Mathf.Abs(v.x) > 0.05f) main.facing = v.x > 0f ? 1 : -1;
            }
            main.tx = main.x;
            main.ty = main.y;
            if (v.sqrMagnitude > 0.01f && Time.unscaledTime - _lastFlySound > 0.67f) { _lastFlySound = Time.unscaledTime; BbSfx.Play(BbSfx.Fly, 0.2f); }
            main.view.animSpeed = MainAnimSpeed;
            if (!main.dying && !main.ghost) main.view.SetState(BbCharacterView.PoseState.Fly);
            if (main.dying) DyingStep(main, dt, true);
            for (int i = 1; i < _actors.Count; i++) SmoothRemote(_actors[i], dt, true);
            var gone = new List<string>();
            foreach (var kv in _dummies)
            {
                SmoothRemote(kv.Value, dt, false);
                if (kv.Value.dying && kv.Value.dyingAge > 99f) gone.Add(kv.Key);
            }
            foreach (string key in gone) { Kill(_dummies[key].root.gameObject); _dummies.Remove(key); }
            SortDepth();
            Layout(false, dt);
        }

        /// <summary>The picture glides to where the server says the character or monster is; a falling body follows its throw; the HP bar of a monster follows its HP.</summary>
        void SmoothRemote(Actor a, float dt, bool party)
        {
            if (a.dying) { DyingStep(a, dt, party); return; }
            if (a.ghost)
            {
                a.view.SetState(BbCharacterView.PoseState.Stand);
                GameObject g;
                if (_ghosts.TryGetValue(a, out g) && g != null) { var f = g.transform.Find("Flare"); if (f != null) f.gameObject.SetActive(Time.frameCount % 4 != 0); }
                return;
            }
            if (a.remote)
            {
                float k = 1f - Mathf.Exp(-14f * dt);
                a.x = Mathf.Lerp(a.x, a.tx, k);
                a.y = Mathf.Lerp(a.y, a.ty, k);
            }
            if (a.locked) a.view.SetState(BbCharacterView.PoseState.Stand);
            else a.view.SetState(BbCharacterView.PoseState.Fly);
            a.view.animSpeed = party ? 1.4f : 1.2f;
            if (a.chargeGlow != null)
            {
                bool charging = Time.unscaledTime < a.chargeUntil;
                a.chargeGlow.gameObject.SetActive(charging);
                if (charging)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f);
                    float grow = 1f - (a.chargeUntil - Time.unscaledTime) / 3f;                        // it gets brighter as the blast would come
                    a.chargeGlow.color = new Color(1f, 0.95f, 0.8f, Mathf.Clamp01(0.15f + 0.45f * pulse * (0.4f + 0.6f * Mathf.Clamp01(grow))));
                    a.chargeGlow.rectTransform.localScale = Vector3.one * (1f + 0.12f * pulse);
                    a.view.SetState(BbCharacterView.PoseState.Stand);
                }
            }
            if (a.hpFill != null)
            {
                float w = a.hpBack.rectTransform.sizeDelta.x - 2f;
                a.hpFill.rectTransform.sizeDelta = new Vector2(w * a.hpRatio, a.hpFill.rectTransform.sizeDelta.y);
                a.hpFill.color = a.hpRatio > 0.5f ? new Color(0.30f, 0.88f, 0.28f) : a.hpRatio > 0.25f ? new Color(0.95f, 0.85f, 0.2f) : new Color(0.95f, 0.30f, 0.2f);
            }
        }

        /// <summary>
        /// The throw of a body that died (Nro Mob.startDie / Char.updateCharDeadFly): up, then away from the blow and down, blinking in the end; a monster is removed when it is
        /// done, a character of the party stays as a ghost (<see cref="SetGhost"/>).
        /// </summary>
        void DyingStep(Actor a, float dt, bool party)
        {
            a.dyingAge += dt;
            a.dyingVelocity.y -= DeathGravity * dt;
            a.dyingVelocity.x *= Mathf.Exp(-1.6f * dt);
            a.x = Mathf.Clamp(a.x + a.dyingVelocity.x * dt, ArenaMargin, _map.Width - ArenaMargin);
            a.y += a.dyingVelocity.y * dt;
            float floor = party ? (InArena ? a.deathY : ArenaFeetMin + 40f) : -60f;
            if (party)
            {
                if (a.y <= floor && a.dyingVelocity.y < 0f)
                {
                    a.y = floor;
                    if (InArena) a.x = a.deathX;                                  // the body is where the server has it: the healer who comes to revive it is measured from there
                    a.dying = false; a.ghost = true; SetGhostLook(a, true);
                }
                return;
            }
            bool blink = a.dyingAge > 0.45f && Mathf.FloorToInt(a.dyingAge * 15f) % 3 == 0;          // Nro: not painted on one tick out of three
            if (a.view != null) a.view.gameObject.SetActive(!blink);
            if (a.dyingAge > 1.4f || a.y < floor) a.dyingAge = 100f;
        }

        /// <summary>Everybody is drawn in order of height: the higher one behind (a lower one is in front).</summary>
        void SortDepth()
        {
            var list = new List<Actor>(_actors.Count + _dummies.Count);
            list.AddRange(_actors);
            foreach (var kv in _dummies) list.Add(kv.Value);
            list.Sort((p, q) => q.y.CompareTo(p.y));
            var order = new string[list.Count];
            bool same = order.Length == _depthOrder.Length;
            for (int i = 0; i < list.Count; i++)
            {
                order[i] = list[i].root.GetInstanceID().ToString();
                if (same && _depthOrder[i] != order[i]) same = false;
            }
            if (same) return;
            _depthOrder = order;
            for (int i = 0; i < list.Count; i++) list[i].root.SetSiblingIndex(1 + i);              // 0 is the floor
        }

        // ---------------------------------------------------------------------------------------------------------------- death of a character of the party

        /// <summary>A character of the party died (Nro Char.startDie): it is thrown away from the blow, then lies as a ghost until it is revived.</summary>
        public void DieParty(ulong characterId, Vector2 away)
        {
            foreach (var a in _actors)
            {
                if (a.id != characterId || a.dying || a.ghost) continue;
                a.dying = true;
                a.dyingAge = 0f;
                float side = Mathf.Abs(away.x) > 0.05f ? Mathf.Sign(away.x) : -a.facing;
                a.deathX = a.x;
                a.deathY = a.y;
                a.dyingVelocity = new Vector2(side * DeathDrift * (InArena ? 0.25f : 1f), DeathThrow);
                a.locked = true;
                a.view.PlayAction(new[] { (int)NroPose.Hit_Injured }, 5f);
            }
        }

        /// <summary>The character stands up again (revived): the ghost goes away.</summary>
        public void Revive(ulong characterId)
        {
            foreach (var a in _actors)
            {
                if (a.id != characterId) continue;
                a.dying = false;
                a.ghost = false;
                a.locked = false;
                SetGhostLook(a, false);
                a.view.StopAction();
            }
        }

        public bool IsGhost(ulong characterId)
        {
            foreach (var a in _actors) if (a.id == characterId) return a.ghost || a.dying;
            return false;
        }

        GameObject MakeGhost(Actor a)
        {
            // Nro Char.paint at statusMe 14: the head over a ghost body (small image 834), a halo (79) above the head, and a yellow glow that blinks (not drawn on one tick of four)
            var root = NewRect("Ghost", a.root);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(10f, 10f);
            root.anchoredPosition = Vector2.zero;
            float s = characterScale;
            float top = Mathf.Max(60f, a.view.FigureHeight);
            var body = BbSkillFx.Pic(834);
            var halo = BbSkillFx.Pic(79);
            if (body != null)
            {
                var im = GhostImage(root, "GhostBody", body, s, 0f);
                im.rectTransform.pivot = new Vector2(0.5f, 1f);
                im.rectTransform.anchoredPosition = new Vector2(0f, top - 20f * s);
            }
            if (halo != null) GhostImage(root, "Halo", halo, s, top + 6f * s);
            var flare = NewRect("Flare", root);
            flare.anchorMin = flare.anchorMax = new Vector2(0.5f, 0f);
            flare.pivot = new Vector2(0.5f, 0.5f);
            flare.sizeDelta = new Vector2(30f * s, 30f * s);
            flare.anchoredPosition = new Vector2(0f, top + 24f * s);
            var fi = flare.gameObject.AddComponent<Image>();
            fi.sprite = flareSprite != null ? flareSprite : dotSprite;
            fi.color = new Color(1f, 1f, 0.6f, 0.95f);
            fi.raycastTarget = false;
            return root.gameObject;
        }

        Image GhostImage(RectTransform parent, string name, Sprite sprite, float uiScale, float y)
        {
            var r = NewRect(name, parent);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = BbSkillFx.SizeOf(sprite, uiScale);
            r.anchoredPosition = new Vector2(0f, y);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        readonly Dictionary<Actor, GameObject> _ghosts = new Dictionary<Actor, GameObject>();

        void SetGhostLook(Actor a, bool on)
        {
            GameObject g;
            a.view.HeadOnly = on;
            if (on)
            {
                if (!_ghosts.TryGetValue(a, out g) || g == null) { g = MakeGhost(a); _ghosts[a] = g; }
                g.SetActive(true);
                a.view.gameObject.SetActive(true);
                a.view.StopAction();
            }
            else if (_ghosts.TryGetValue(a, out g) && g != null) g.SetActive(false);
        }
    }
}
