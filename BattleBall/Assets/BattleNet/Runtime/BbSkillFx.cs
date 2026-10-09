using System;
using System.Collections.Generic;
using System.IO;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The pictures of the skills, from the Nro data (owner request 2026-10-09: "thêm hiệu ứng kỹ năng/frame kỹ năng"). Data: data/skill/skill_fx.json made by
    /// tools/build_assets.py from update_data/effect, dart and skill (GameScr.readEfect, readDart, readSkill): for every painted frame of a skill paint up to three
    /// effects (a list of small images played one per frame, placed from the character) and an arrow (>= 100: a dart that flies to the target).
    /// </summary>
    /// <summary>Destroys a picture at once in the editor tools (no frame passes there), at the end of the frame in the game.</summary>
    public static class BbFxKill
    {
        public static void Go(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go);
        }
    }

    public static class BbSkillFxData
    {
        [Serializable] public class Row { public int[] v; }
        [Serializable] public class EffectRow { public int[] f; }
        [Serializable] public class DartRow { public int nUpdate, va, xd; public int[] tail, tailB, xd1, xd2; public Row[] head, headB; }
        [Serializable] public class PaintRow { public int id, happen; public int[] stand, fly; }
        [Serializable] class FileData { public EffectRow[] effects; public DartRow[] darts; public PaintRow[] paints; }

        static FileData _data;
        static Dictionary<int, PaintRow> _paints;

        static void Load()
        {
            if (_data != null) return;
            _data = new FileData { effects = new EffectRow[0], darts = new DartRow[0], paints = new PaintRow[0] };
            _paints = new Dictionary<int, PaintRow>();
            try
            {
                string file = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "skill", "skill_fx.json");
                if (!File.Exists(file)) return;
                var d = JsonUtility.FromJson<FileData>(File.ReadAllText(file));
                if (d == null) return;
                _data = d;
                if (d.paints != null) foreach (var p in d.paints) _paints[p.id] = p;
            }
            catch (Exception) { }
        }

        public static bool Loaded { get { Load(); return _paints.Count > 0; } }

        /// <summary>The effect with this id (the ids of the skill frames count from 1): image, dx, dy triples; null when there is none.</summary>
        public static int[] Effect(int id)
        {
            Load();
            return id >= 1 && id <= _data.effects.Length ? _data.effects[id - 1].f : null;
        }

        public static DartRow Dart(int id)
        {
            Load();
            return id >= 0 && id < _data.darts.Length ? _data.darts[id] : null;
        }

        /// <summary>The painted frames of a paint (12 ints each) for the standing or the flying list.</summary>
        public static int[] Frames(int paint, bool fly)
        {
            Load();
            PaintRow row;
            if (!_paints.TryGetValue(paint, out row)) return null;
            return fly && row.fly != null && row.fly.Length > 0 ? row.fly : row.stand;
        }

        /// <summary>The first painted frame of a paint that throws a dart: its index, where the hand is (Nro px from the feet: dx, dy up) and the dart; frame -1 = none.</summary>
        public struct Arrow
        {
            public int frame, dart;
            public float dx, up;
        }

        public static Arrow ArrowOf(int paint, bool fly)
        {
            var none = new Arrow { frame = -1, dart = -1 };
            var frames = Frames(paint, fly);
            if (frames == null) return none;
            for (int i = 0; i + 11 < frames.Length; i += 12)
                if (frames[i + 9] >= 100) return new Arrow { frame = i / 12, dart = frames[i + 9] - 100, dx = frames[i + 10] - 10f, up = -frames[i + 11] };
            return none;
        }

        /// <summary>The effect that happens on the target when the skill lands (Nro: effectHappenOnMob, 80 when the paint names none).</summary>
        public static int Happen(int paint)
        {
            Load();
            PaintRow row;
            return _paints.TryGetValue(paint, out row) && row.happen > 0 ? row.happen : 80;
        }

        /// <summary>The dart (projectile picture) a skill of this level shoots, -1 when its paint has none.</summary>
        public static int DartOf(uint skillId, uint level)
        {
            Load();
            int paint = BbSkillPoses.PaintOf(skillId, level);
            PaintRow row;
            if (paint < 0 || !_paints.TryGetValue(paint, out row)) return -1;
            foreach (var list in new[] { row.stand, row.fly })
            {
                if (list == null) continue;
                for (int i = 0; i + 11 < list.Length; i += 12) if (list[i + 9] >= 100) return list[i + 9] - 100;
            }
            return -1;
        }
    }

    /// <summary>Plays the effects of a skill paint on a character of the village, one painted frame per pose (Char.paintCharWithSkill of Nro).</summary>
    public sealed class BbSkillFx : MonoBehaviour
    {
        public BbVillageView village;

        sealed class Layer
        {
            public int[] effect;          // the running effect (image, dx, dy triples)
            public int index;             // the next picture of it
            public float dx, dy;          // Nro px from the character, fixed when the effect starts
            public Image image;
        }

        sealed class Run
        {
            public ulong character;
            public int paint;
            public bool fly;
            public int[] frames;          // 12 ints per painted frame
            public int fxFrom;            // the pose of the action where the first painted frame is shown
            public int poseCount;
            public float start, perPose;
            public int next;
            public readonly Layer[] layers = { new Layer(), new Layer(), new Layer() };
            public float facing = 1f;
        }

        readonly List<Run> _runs = new List<Run>();

        sealed class Hit
        {
            public int[] effect;
            public Vector2 feet;
            public float start;
            public Image image;
        }

        readonly List<Hit> _hits = new List<Hit>();

        /// <summary>The effect of a skill landing on a target: one picture per frame, bottom centre on the target's feet (Char.paint of Nro, effPaints).</summary>
        public void PlayHit(int effectId, Vector2 feet)
        {
            var effect = BbSkillFxData.Effect(effectId);
            if (effect == null || effect.Length < 3) return;
            _hits.Add(new Hit { effect = effect, feet = feet, start = Time.unscaledTime });
        }

        void TickHits()
        {
            if (village == null || village.WorldLayer == null) return;
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                var h = _hits[i];
                int index = Mathf.FloorToInt((Time.unscaledTime - h.start) * BbSkillPoses.FramesPerSecond);
                if (index * 3 >= h.effect.Length)
                {
                    if (h.image != null) BbFxKill.Go(h.image.gameObject);
                    _hits.RemoveAt(i);
                    continue;
                }
                var sprite = Pic(h.effect[index * 3]);
                if (h.image == null)
                {
                    var go = new GameObject("SkillHit", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(village.WorldLayer, false);
                    h.image = go.GetComponent<Image>();
                    h.image.raycastTarget = false;
                    var rt0 = h.image.rectTransform;
                    rt0.anchorMin = rt0.anchorMax = Vector2.zero;
                    rt0.pivot = new Vector2(0.5f, 0f);
                }
                h.image.enabled = sprite != null;
                if (sprite == null) continue;
                h.image.sprite = sprite;
                h.image.rectTransform.sizeDelta = SizeOf(sprite, village.UiScale);
                h.image.rectTransform.anchoredPosition = h.feet;
                h.image.transform.SetAsLastSibling();
            }
        }

        /// <summary>For the screenshot tool: the hit effects shown at this frame.</summary>
        public void DemoHitAt(int frame)
        {
            foreach (var h in _hits) h.start = Time.unscaledTime - (frame + 0.5f) / BbSkillPoses.FramesPerSecond;
            TickHits();
        }

        /// <summary>The paint frames of a skill start at the pose {@code fxFrom} of an action of {@code seconds}.</summary>
        public void Play(ulong character, int paint, bool fly, int fxFrom, int poseCount, float seconds)
        {
            var frames = BbSkillFxData.Frames(paint, fly);
            if (frames == null || frames.Length == 0 || poseCount <= 0) return;
            _runs.RemoveAll(r => { if (r.character == character) { Clear(r); return true; } return false; });
            _runs.Add(new Run { character = character, paint = paint, fly = fly, frames = frames, fxFrom = fxFrom, poseCount = poseCount, start = Time.unscaledTime, perPose = seconds / poseCount });
        }

        void Update()
        {
            Tick();
        }

        /// <summary>For the screenshot tool: the frame of every running effect shown as the action reaches this pose.</summary>
        public void DemoAt(float pose)
        {
            foreach (var r in _runs) r.start = Time.unscaledTime - (pose + 0.5f) * r.perPose;
            Tick();
        }

        public void Tick()
        {
            if (village == null) return;
            TickHits();
            float now = Time.unscaledTime;
            for (int i = _runs.Count - 1; i >= 0; i--)
            {
                var r = _runs[i];
                ulong id;
                Vector2 feet;
                int facing = 1, index = 0;
                bool found = false;
                for (; index < village.PartyCount; index++)
                    if (village.PartyMember(index, out id, out feet, out facing) && id == r.character) { found = true; break; }
                if (!found) { Clear(r); _runs.RemoveAt(i); continue; }
                village.PartyMember(index, out id, out feet, out facing);
                r.facing = facing >= 0 ? 1f : -1f;
                int pose = Mathf.Min(r.poseCount - 1, Mathf.FloorToInt((now - r.start) / Mathf.Max(0.0001f, r.perPose)));
                while (r.next <= pose)
                {
                    Advance(r, r.next - r.fxFrom);
                    r.next++;
                }
                Place(r, feet);
                if (now - r.start > r.perPose * r.poseCount + 0.05f) { Clear(r); _runs.RemoveAt(i); }
            }
        }

        /// <summary>One painted frame: effects start where the frame names them, every running effect shows its next picture.</summary>
        void Advance(Run r, int frame)
        {
            bool inside = frame >= 0 && (frame + 1) * 12 <= r.frames.Length;
            for (int s = 0; s < 3; s++)
            {
                var l = r.layers[s];
                if (!inside) l.effect = null;                       // the skill is over (or not begun): its effects stop with it
                else
                {
                    int effectId = r.frames[frame * 12 + s * 3];
                    if (effectId != 0)
                    {
                        l.effect = BbSkillFxData.Effect(effectId);
                        l.index = 0;
                        l.dx = l.dy = 0f;
                    }
                    if (l.effect != null)
                    {
                        if (l.dx == 0f) l.dx = r.frames[frame * 12 + s * 3 + 1];
                        if (l.dy == 0f) l.dy = r.frames[frame * 12 + s * 3 + 2];
                    }
                }
                if (l.effect == null || l.index * 3 + 2 >= l.effect.Length)
                {
                    l.effect = null;
                    if (l.image != null) l.image.enabled = false;
                    continue;
                }
                int pic = l.effect[l.index * 3];
                var sprite = Pic(pic);
                if (l.image == null || l.image.transform.parent != village.WorldLayer)
                {
                    if (l.image != null) BbFxKill.Go(l.image.gameObject);
                    var go = new GameObject("SkillFx", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(village.WorldLayer, false);
                    l.image = go.GetComponent<Image>();
                    l.image.raycastTarget = false;
                }
                l.image.enabled = sprite != null;
                if (sprite != null) { l.image.sprite = sprite; l.image.rectTransform.sizeDelta = SizeOf(sprite, village.UiScale); }
                l.image.transform.SetAsLastSibling();
                l.index++;
                // the offset of the picture inside the effect is read again at every picture: keep it in the layer
                l.image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _offset[l] = new Vector2(l.effect[(l.index - 1) * 3 + 1], l.effect[(l.index - 1) * 3 + 2]);
                if (l.index * 3 + 2 >= l.effect.Length) l.effect = null;
            }
        }

        readonly Dictionary<Layer, Vector2> _offset = new Dictionary<Layer, Vector2>();

        void Place(Run r, Vector2 feet)
        {
            float scale = village.UiScale;
            foreach (var l in r.layers)
            {
                if (l.image == null || !l.image.enabled) continue;
                Vector2 o;
                _offset.TryGetValue(l, out o);
                // Nro: x + dx + the picture's dx (mirrored when it faces left), y + dy (y grows downward there)
                float x = feet.x + r.facing * (l.dx + o.x) * scale;
                float y = feet.y - (l.dy + o.y) * scale;
                var rt = l.image.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.anchoredPosition = new Vector2(x, y);
                rt.localScale = new Vector3(r.facing, 1f, 1f);
            }
        }

        void Clear(Run r)
        {
            foreach (var l in r.layers)
            {
                _offset.Remove(l);
                if (l.image != null) BbFxKill.Go(l.image.gameObject);
                l.image = null;
            }
        }

        public static Sprite Pic(int id)
        {
            var resolver = NroSpriteResolver.Instance;
            return id > 0 && resolver != null ? resolver.ResolveSprite(id, BbAssetCache.Scale) : null;
        }

        /// <summary>The size of a small image on the canvas: its pixels are Nro pixels times the asset scale; the village draws an Nro pixel as {@code uiScale} px.</summary>
        public static Vector2 SizeOf(Sprite sprite, float uiScale)
        {
            float k = uiScale / Mathf.Max(1, BbAssetCache.Scale);
            return new Vector2(sprite.rect.width * k, sprite.rect.height * k);
        }
    }

    /// <summary>
    /// The picture of a projectile: a dart of Nro (PlayerDart.paint): the head (its picture follows the direction, and changes every game tick), its border and the tail
    /// of pictures left behind, each changing as it ages, with sparks. The server reports where the projectile is 20 times a second; the picture is drawn every frame
    /// from the last two reports (it keeps flying at the speed they show), so that it glides instead of jumping (owner remark 2026-10-09: the flight looked broken up).
    /// It starts at the hand of the character when the painted frame that throws it is reached, and catches up with the server's projectile from there.
    /// {@code Fly} makes a dart that flies by itself (skills that throw something without a server projectile: Trói, Thôi miên, Biến Sôcôla).
    /// </summary>
    public sealed class BbDart : MonoBehaviour
    {
        static readonly sbyte[] Frame = { 0, 1, 2, 1, 0, 1, 2, 1, 0, 1, 2, 1, 0, 1, 2, 1, 0, 1, 2, 1, 0, 1, 2, 1, 0 };
        static readonly int[] Transform = { 0, 0, 0, 7, 6, 6, 6, 2, 2, 3, 3, 4, 5, 5, 5, 1 };
        static readonly int[] ArrowIndex = { 0, 15, 37, 52, 75, 105, 127, 142, 165, 195, 217, 232, 255, 285, 307, 322, 345, 370 };

        sealed class Piece
        {
            public Vector2 at;
            public int age;
            public Image border, main, spark;
        }

        BbSkillFxData.DartRow _dart;
        float _scale;
        Image _head, _headBorder;
        readonly List<Piece> _tail = new List<Piece>();
        float _clock;
        int _tick;
        Vector2 _tickPos;
        bool _hasTickPos;
        // what the server said
        Vector2 _server, _velocity, _dirVec = Vector2.right;
        float _serverTime;
        bool _haveServer, _manual;
        // the start at the hand
        Vector2 _hand, _offset;
        float _showAt;
        bool _useHand, _shown;
        // a dart that flies by itself
        bool _fly;
        Vector2 _from, _to;
        float _flyStart, _flySpeed;

        public static BbDart Create(Transform parent, int dartId, float uiScale)
        {
            var dart = BbSkillFxData.Dart(dartId);
            if (dart == null) return null;
            var go = new GameObject("Dart" + dartId, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<BbDart>();
            d._dart = dart;
            d._scale = uiScale;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            d._headBorder = d.NewImage("HeadBorder");
            d._head = d.NewImage("Head");
            return d;
        }

        /// <summary>The dart appears at {@code hand} after {@code delay} seconds (the frame of the throw) and then catches up with the projectile of the server.</summary>
        public void Begin(Vector2 hand, float delay)
        {
            _hand = hand;
            _useHand = true;
            _showAt = Time.unscaledTime + Mathf.Max(0f, delay);
            SetVisible(false);
        }

        /// <summary>A dart that flies from {@code from} to {@code to} by itself after {@code delay} seconds ({@code nroPerSecond}: Nro pixels a second at the top speed).</summary>
        public void Fly(Vector2 from, Vector2 to, float delay, float nroPerSecond)
        {
            _fly = true;
            _from = from;
            _to = to;
            _flyStart = Time.unscaledTime + Mathf.Max(0f, delay);
            _flySpeed = nroPerSecond * _scale;
            SetVisible(false);
        }

        Image NewImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            return img;
        }

        void SetVisible(bool on)
        {
            _head.enabled = on && _head.sprite != null;
            _headBorder.enabled = on && _headBorder.sprite != null;
            foreach (var p in _tail)
            {
                if (p.main != null) p.main.enabled = on && p.main.sprite != null;
                if (p.border != null) p.border.enabled = on && p.border.sprite != null;
                if (p.spark != null) p.spark.enabled = on && p.spark.sprite != null;
            }
        }

        static int DirIndex(float visualAngle)
        {
            int a = Mathf.RoundToInt(Mathf.Repeat(visualAngle, 360f));
            for (int i = 0; i < ArrowIndex.Length - 1; i++)
                if (a >= ArrowIndex[i] && a <= ArrowIndex[i + 1]) return i >= 16 ? 0 : i;
            return 0;
        }

        static void Show(Image img, int pic, Vector2 at, float scale, int transform)
        {
            var sprite = BbSkillFx.Pic(pic);
            img.enabled = sprite != null;
            if (sprite == null) return;
            img.sprite = sprite;
            var rt = img.rectTransform;
            rt.sizeDelta = BbSkillFx.SizeOf(sprite, scale);
            rt.anchoredPosition = at;
            // the J2ME transforms of Nro: mirror first, then rotate clockwise
            bool mirror = transform == 2 || transform == 4 || transform == 7;
            float cw = transform == 5 || transform == 7 ? 90f : transform == 3 ? 180f : transform == 6 || transform == 4 ? 270f : 0f;
            if (transform == 1) { mirror = true; cw = 180f; }
            rt.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            rt.localRotation = Quaternion.Euler(0f, 0f, -cw);
        }

        /// <summary>The server reports the projectile at {@code at} flying along (dirX, dirY). With {@code dt} (screenshot tool) it is drawn at once.</summary>
        public void Move(Vector2 at, float dirX, float dirY, float dt = -1f)
        {
            if (dirX != 0f || dirY != 0f) _dirVec = new Vector2(dirX, dirY).normalized;
            if (dt >= 0f)
            {
                _manual = true;
                Render(at, dt);
                return;
            }
            float now = Time.unscaledTime;
            if (!_haveServer) { _server = at; _serverTime = now; _haveServer = true; return; }
            if ((at - _server).sqrMagnitude > 0.01f)
            {
                float span = now - _serverTime;
                if (span > 0.001f) _velocity = Vector2.Lerp(_velocity, (at - _server) / span, _velocity == Vector2.zero ? 1f : 0.6f);
                _server = at;
                _serverTime = now;
            }
        }

        void Update()
        {
            if (_manual) return;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            if (_fly)
            {
                if (now < _flyStart) return;
                float t = now - _flyStart;
                // the dart of Nro speeds up as it goes: from 40% to the full speed in a quarter of a second
                float travelled = _flySpeed * (t < 0.25f ? 0.4f * t + 0.6f * t * t * 2f : 0.4f * t + 0.6f * (t - 0.125f));
                Vector2 line = _to - _from;
                if (travelled >= line.magnitude) { BbFxKill.Go(gameObject); return; }
                _dirVec = line.normalized;
                Render(_from + _dirVec * travelled, dt);
                return;
            }
            if (!_haveServer) return;
            Vector2 at = _server + _velocity * Mathf.Min(0.15f, now - _serverTime);
            if (_useHand)
            {
                if (now < _showAt) return;
                if (!_shown) { _shown = true; _offset = _hand - at; }
                _offset = Vector2.Lerp(_offset, Vector2.zero, 1f - Mathf.Exp(-14f * dt));
                at += _offset;
            }
            Render(at, dt);
        }

        void Render(Vector2 at, float dt)
        {
            float visual = Mathf.Atan2(_dirVec.y, _dirVec.x) * Mathf.Rad2Deg;
            int dir = DirIndex(visual);
            int frame = Frame[dir];
            int transform = Transform[dir];
            // the game tick of Nro runs at about 30 per second: the tail ages and the head changes its picture at that pace
            _clock += dt;
            int ticks = Mathf.FloorToInt(_clock * BbSkillPoses.FramesPerSecond);
            if (!_hasTickPos) { _tickPos = at; _hasTickPos = true; }
            if (ticks > _tick)
            {
                int steps = Mathf.Min(3, ticks - _tick);
                _tick = ticks;
                for (int s = 0; s < steps; s++) Age(at);
                _tickPos = at;
            }
            int heads = _dart.head.Length, borders = _dart.headB.Length;
            if (borders > 0) Show(_headBorder, At(_dart.headB[_tick % borders].v, frame), at, _scale, transform);
            if (heads > 0) Show(_head, At(_dart.head[_tick % heads].v, frame), at, _scale, transform);
            _headBorder.transform.SetAsFirstSibling();
            _head.transform.SetAsLastSibling();
            foreach (var p in _tail) DrawPiece(p);
        }

        static int At(int[] list, int i)
        {
            return list != null && list.Length > 0 ? list[Mathf.Min(i, list.Length - 1)] : 0;
        }

        void Age(Vector2 at)
        {
            for (int i = _tail.Count - 1; i >= 0; i--)
            {
                _tail[i].age++;
                if (_tail[i].age >= _dart.tail.Length)
                {
                    Kill(_tail[i]);
                    _tail.RemoveAt(i);
                }
            }
            if (_dart.tail.Length == 0) return;
            int n = Mathf.Max(1, _dart.nUpdate);
            for (int k = 1; k <= n; k++)
            {
                // the pieces of one tick are spread along the way the head made since the last tick: a tail without gaps
                var piece = new Piece { at = Vector2.Lerp(_tickPos, at, k / (float)n) };
                piece.border = NewImage("TailBorder");
                piece.main = NewImage("Tail");
                piece.spark = NewImage("Spark");
                piece.border.transform.SetAsFirstSibling();
                _tail.Add(piece);
            }
        }

        void DrawPiece(Piece p)
        {
            int i = Mathf.Min(p.age, _dart.tail.Length - 1);
            Show(p.border, i < _dart.tailB.Length ? _dart.tailB[i] : 0, p.at, _scale, 0);
            Show(p.main, _dart.tail[i], p.at, _scale, 0);
            bool spark = UnityEngine.Random.Range(0, 100) < _dart.xd;
            int[] list = _tick % 2 != 0 ? _dart.xd2 : _dart.xd1;
            Show(p.spark, spark && list != null && i < list.Length ? list[i] : 0, p.at, _scale, 0);
            p.main.transform.SetAsLastSibling();
        }

        static void Kill(Piece p)
        {
            if (p.border != null) BbFxKill.Go(p.border.gameObject);
            if (p.main != null) BbFxKill.Go(p.main.gameObject);
            if (p.spark != null) BbFxKill.Go(p.spark.gameObject);
        }
    }
}
