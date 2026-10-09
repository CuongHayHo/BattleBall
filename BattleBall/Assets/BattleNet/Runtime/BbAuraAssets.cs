using System.Collections.Generic;
using System.IO;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>One picture of an aura layer cut into its equal frames (one on top of the other in the file, the first one at the top).</summary>
    public sealed class AuraArt
    {
        public Texture2D texture;
        public Sprite[] frames;
        public float zoom = 1f;                 // the scale of the file (x1..x4): the sprites have 100 x zoom pixels per unit
        public float lastUsed;
        public long bytes;

        public bool Alive { get { return texture != null && frames != null && frames.Length > 0 && frames[0] != null; } }
        /// <summary>The size of one frame in Nro pixels (the size of the file at x1).</summary>
        public Vector2 NroSize { get { return frames[0].rect.size / zoom; } }
    }

    /// <summary>
    /// Loads the pictures of the auras (data/aura/x{N}/aura_{id}_{layer}.png of the asset folder: SourceData in the Editor, the downloaded cache on a device).
    /// The files are tall strips (the frames one on top of the other, cut into equal parts), so they are big: only the recently used ones stay in memory.
    /// </summary>
    public static class BbAuraAssets
    {
        const long Budget = 48L * 1024 * 1024;          // bytes of textures kept; the ones not used for a moment are thrown out when a new one is loaded
        static readonly Dictionary<string, AuraArt> Cache = new Dictionary<string, AuraArt>();
        static readonly HashSet<string> Missing = new HashSet<string>();

        /// <summary>The frames of one layer of an aura, or null when the picture is not in the assets.</summary>
        public static AuraArt Get(int auraId, int layer, int frames)
        {
            frames = Mathf.Max(1, frames);
            int scale = BbAssetCache.Scale;
            string key = auraId + "_" + layer + "_" + frames + "_" + scale;
            AuraArt art;
            if (Cache.TryGetValue(key, out art))
            {
                if (art.Alive) { art.lastUsed = Time.unscaledTime; return art; }
                Cache.Remove(key);
            }
            if (Missing.Contains(key)) return null;
            art = Load(auraId, layer, frames, scale);
            if (art == null) { Missing.Add(key); return null; }
            art.lastUsed = Time.unscaledTime;
            Cache[key] = art;
            Trim(art);
            return art;
        }

        static readonly Dictionary<int, Sprite> Thumbs = new Dictionary<int, Sprite>();

        /// <summary>
        /// A small still picture of the aura for the Aura tab (data/aura/thumb/aura_{id}.png, 128 x 128, made by build_assets.py): tiny files that load at once, kept for good.
        /// When the file is not there the first frame of the first layer is used instead (loads the big picture).
        /// </summary>
        public static Sprite GetThumb(AuraInfo aura)
        {
            int id = (int)aura.AuraId;
            Sprite sprite;
            if (Thumbs.TryGetValue(id, out sprite) && sprite != null) return sprite;
            try
            {
                string path = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "aura", "thumb", "aura_" + id + ".png");
                if (File.Exists(path))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
                    {
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
                        Thumbs[id] = sprite;
                        return sprite;
                    }
                    Object.DestroyImmediate(tex);
                }
            }
            catch (System.Exception) { /* fall back below */ }
            foreach (var layer in aura.Layers)
            {
                var art = Get(id, (int)layer.Layer, (int)layer.Frames);
                if (art != null) return art.frames[0];
            }
            return null;
        }

        static AuraArt Load(int auraId, int layer, int frames, int scale)
        {
            string root;
            try { root = NroSpriteResolver.GetDefaultServerRoot(); }
            catch (System.Exception) { return null; }
            int[] order = { scale, 4, 3, 2, 1 };
            foreach (int s in order)
            {
                string path = Path.Combine(root, "data", "aura", "x" + s, "aura_" + auraId + "_" + layer + ".png");
                if (!File.Exists(path)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); continue; }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                int h = tex.height, w = tex.width;
                var sprites = new Sprite[frames];
                for (int f = 0; f < frames; f++)
                {
                    int top = Mathf.RoundToInt(f * h / (float)frames), bottom = Mathf.RoundToInt((f + 1) * h / (float)frames);
                    sprites[f] = Sprite.Create(tex, new Rect(0, h - bottom, w, Mathf.Max(1, bottom - top)), new Vector2(0.5f, 0f), 100f * s);
                }
                return new AuraArt { texture = tex, frames = sprites, zoom = s, bytes = (long)w * h * 4 };
            }
            return null;
        }

        static void Trim(AuraArt keep)
        {
            long total = 0;
            foreach (var a in Cache.Values) total += a.bytes;
            if (total <= Budget) return;
            var order = new List<KeyValuePair<string, AuraArt>>(Cache);
            order.Sort((x, y) => x.Value.lastUsed.CompareTo(y.Value.lastUsed));
            float now = Time.unscaledTime;
            foreach (var kv in order)
            {
                if (total <= Budget) break;
                if (kv.Value == keep || now - kv.Value.lastUsed < 2f) continue;           // the ones on screen refresh lastUsed every frame
                total -= kv.Value.bytes;
                Destroy(kv.Value);
                Cache.Remove(kv.Key);
            }
        }

        static void Destroy(AuraArt a)
        {
            if (a.frames != null) foreach (var s in a.frames) if (s != null) DestroyObject(s);
            if (a.texture != null) DestroyObject(a.texture);
        }

        static void DestroyObject(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        /// <summary>Forgets every picture (the scale changed, or the assets were updated).</summary>
        public static void Clear()
        {
            foreach (var t in Thumbs.Values) if (t != null) { var tex = t.texture; DestroyObject(t); if (tex != null) DestroyObject(tex); }
            Thumbs.Clear();
            foreach (var a in Cache.Values) Destroy(a);
            Cache.Clear();
            Missing.Clear();
        }
    }

    /// <summary>
    /// The pictures of one aura drawn as Nro draws them (Char.paintAuraBehind and paintAuraFront): layer 0 behind the character, layer 1 in front of it (2 Nro pixels lower),
    /// both with the bottom centre of the frame on the feet, the frame changing every 2 game ticks of 25 per second (Nro: every 4, the owner asked for twice as fast). It needs a parent whose origin is the feet.
    /// The owner calls <see cref="Tick"/>.
    /// </summary>
    public sealed class BbAuraRender
    {
        // Nro changes the frame every 4 game ticks of 25 per second (6.25 per second); the owner asked for twice as fast (2026-10-08)
        public const float FramesPerSecond = 2f * 25f / 4f;

        readonly Image _back, _front;
        readonly List<KeyValuePair<int, int>> _layers = new List<KeyValuePair<int, int>>();        // layer, frames
        AuraArt _backArt, _frontArt;
        int _id;
        float _clock;
        bool _visible = true;
        // what is drawn now: the images are touched only when one of these changes (a frame turns, the scale or the box changes), not on every frame of the game
        bool _dirty = true;
        int _drawnBack = -1, _drawnFront = -1;
        float _drawnScale = -1f;
        Vector2 _drawnBox = new Vector2(-1f, -1f);

        public float uiScale = 2f;                     // UI pixels per Nro pixel
        public Vector2 fitBox;                         // when both parts are above 0 the aura is scaled to fit this box instead (the thumbnails)

        public int AuraId { get { return _id; } }
        /// <summary>Height of the tallest loaded frame in Nro pixels (0 without an aura).</summary>
        public float NroHeight
        {
            get
            {
                float h = 0f;
                if (_backArt != null && _backArt.Alive) h = Mathf.Max(h, _backArt.NroSize.y);
                if (_frontArt != null && _frontArt.Alive) h = Mathf.Max(h, _frontArt.NroSize.y);
                return h;
            }
        }

        public BbAuraRender(Transform parent)
        {
            _back = NewImage("AuraBack", parent);
            _front = NewImage("AuraFront", parent);
            Reorder();
        }

        static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.enabled = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            return img;
        }

        /// <summary>The back layer goes under everything of the parent, the front layer over it.</summary>
        public void Reorder()
        {
            _back.transform.SetAsFirstSibling();
            _front.transform.SetAsLastSibling();
        }

        public void Set(int auraId, IList<AuraLayer> layers)
        {
            _id = auraId;
            _layers.Clear();
            _backArt = _frontArt = null;
            if (auraId > 0 && layers != null)
                foreach (var l in layers) _layers.Add(new KeyValuePair<int, int>((int)l.Layer, (int)l.Frames));
            _clock = 0f;
            _dirty = true;
            Apply();
        }

        public void SetVisible(bool visible)
        {
            if (visible == _visible) return;
            _visible = visible;
            _dirty = true;
            Apply();
        }

        int FrameOf(AuraArt a)
        {
            if (a == null || !a.Alive) return -1;
            int n = a.frames.Length;
            return ((int)_clock % n + n) % n;
        }

        public void Tick(float dt)
        {
            if (_id <= 0) return;
            _clock += dt * FramesPerSecond;
            if (!_dirty && _drawnScale == uiScale && _drawnBox == fitBox && FrameOf(_backArt) == _drawnBack && FrameOf(_frontArt) == _drawnFront)
            {
                float now = Time.unscaledTime;                                   // nothing to redraw: only keep the pictures from being thrown out of the cache
                if (_backArt != null) _backArt.lastUsed = now;
                if (_frontArt != null) _frontArt.lastUsed = now;
                return;
            }
            Apply();
        }

        void Apply()
        {
            bool show = _visible && _id > 0;
            _backArt = show ? Fetch(0, _backArt) : null;
            _frontArt = show ? Fetch(1, _frontArt) : null;
            float scale = 0f;
            if (fitBox.x > 0f && fitBox.y > 0f)
            {
                float maxW = 1f, maxH = 1f;
                foreach (var a in new[] { _backArt, _frontArt })
                    if (a != null) { maxW = Mathf.Max(maxW, a.frames[0].rect.width); maxH = Mathf.Max(maxH, a.frames[0].rect.height); }
                scale = Mathf.Min(fitBox.x / maxW, fitBox.y / maxH);
            }
            Draw(_back, _backArt, scale, 0f);
            Draw(_front, _frontArt, scale, scale > 0f ? 0f : -2f * uiScale);
            _drawnBack = FrameOf(_backArt);
            _drawnFront = FrameOf(_frontArt);
            _drawnScale = uiScale;
            _drawnBox = fitBox;
            _dirty = false;
        }

        AuraArt Fetch(int layer, AuraArt current)
        {
            if (current != null && current.Alive) { current.lastUsed = Time.unscaledTime; return current; }
            int frames = 0;
            foreach (var kv in _layers) if (kv.Key == layer) frames = kv.Value;
            return frames > 0 ? BbAuraAssets.Get(_id, layer, frames) : null;
        }

        void Draw(Image img, AuraArt art, float fitScale, float dy)
        {
            if (art == null || !art.Alive) { img.enabled = false; return; }
            int n = art.frames.Length;
            var sprite = art.frames[((int)_clock % n + n) % n];
            img.enabled = true;
            img.sprite = sprite;
            img.rectTransform.sizeDelta = fitScale > 0f ? sprite.rect.size * fitScale : sprite.rect.size * (uiScale / art.zoom);
            img.rectTransform.anchoredPosition = new Vector2(0f, dy);
        }
    }
}
