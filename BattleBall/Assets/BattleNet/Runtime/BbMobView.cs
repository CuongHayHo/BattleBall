using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// Draws an Nro mob (the training dummy Mộc nhân is mob template 0) from the strip made by tools/build_assets.py: data/mob/strip/{id}.json (frame size and feet in
    /// Nro pixels) and data/mob/strip/x{N}/{id}.png (the frames side by side at scale N). The origin of the RectTransform is the feet. Frame 0 stands; a hit shows the
    /// last two frames for a moment (Mộc nhân: the hat knocked aside), as the Nro client does for a mob that is hurt.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BbMobView : MonoBehaviour
    {
        [Serializable]
        class StripInfo { public int id, frames, w, h, ax, ay; }

        public float uiScale = 2.2f;              // UI pixels per Nro pixel
        public bool loop;                         // a creature that moves all the time (the pet hatched by Đẻ trứng): its frames play one after the other

        static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();
        static readonly Dictionary<int, StripInfo> Infos = new Dictionary<int, StripInfo>();

        Image _image;
        Sprite[] _frames;
        StripInfo _info;
        float _hurtUntil, _clock;
        int _shown = -1;

        /// <summary>Height of the mob in UI pixels (0 until it is loaded): the name and the target arrow go above it.</summary>
        public float Height { get { return _info != null ? _info.h * uiScale : 0f; } }

        public bool Show(int mobId)
        {
            if (_image == null)
            {
                var go = new GameObject("Mob", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                _image = go.GetComponent<Image>();
                _image.raycastTarget = false;
                var r = _image.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            }
            _info = LoadInfo(mobId);
            _frames = _info != null ? LoadFrames(_info) : null;
            _image.enabled = _frames != null;
            if (_frames == null) return false;
            var rt = _image.rectTransform;
            rt.sizeDelta = new Vector2(_info.w, _info.h) * uiScale;
            rt.pivot = new Vector2(_info.ax / (float)_info.w, _info.ay / (float)_info.h);
            rt.anchoredPosition = Vector2.zero;
            _shown = -1;
            SetFrame(0);
            return true;
        }

        /// <summary>The mob is stunned: the hurt frames go on for this long (Mob.update of Nro: isFreez).</summary>
        public void Stun(float seconds)
        {
            _hurtUntil = Time.unscaledTime + seconds;
        }

        /// <summary>The mob was hit: the hurt frames for a moment.</summary>
        public void Hurt()
        {
            _hurtUntil = Time.unscaledTime + 0.35f;
        }

        void Update()
        {
            if (_frames == null) return;
            _clock += Time.unscaledDeltaTime;
            bool hurt = Time.unscaledTime < _hurtUntil && _frames.Length >= 3;
            int moving = _frames.Length > 4 ? _frames.Length - 2 : _frames.Length;        // the last two frames of a mob are for when it is hit
            SetFrame(hurt ? _frames.Length - 2 + ((int)(_clock * 12f) % 2) : loop ? (int)(_clock * 8f) % Mathf.Max(1, moving) : 0);
        }

        void SetFrame(int i)
        {
            if (i == _shown || _frames == null) return;
            _shown = i;
            _image.sprite = _frames[Mathf.Clamp(i, 0, _frames.Length - 1)];
        }

        static StripInfo LoadInfo(int mobId)
        {
            StripInfo info;
            if (Infos.TryGetValue(mobId, out info)) return info;
            try
            {
                string file = Path.Combine(NroRig.NroSpriteResolver.GetDefaultServerRoot(), "data", "mob", "strip", mobId + ".json");
                info = File.Exists(file) ? JsonUtility.FromJson<StripInfo>(File.ReadAllText(file)) : null;
            }
            catch (Exception) { info = null; }
            if (info != null && (info.frames <= 0 || info.w <= 0 || info.h <= 0)) info = null;
            if (info != null) Infos[mobId] = info;
            return info;
        }

        /// <summary>The frames at the scale in use, or the nearest scale that is there.</summary>
        static Sprite[] LoadFrames(StripInfo info)
        {
            int want = BbAssetCache.Scale;
            foreach (int z in new[] { want, 4, 3, 2, 1 })
            {
                string key = info.id + "_x" + z;
                Sprite[] frames;
                if (Cache.TryGetValue(key, out frames)) return frames;
                string file = Path.Combine(NroRig.NroSpriteResolver.GetDefaultServerRoot(), "data", "mob", "strip", "x" + z, info.id + ".png");
                if (!File.Exists(file)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(File.ReadAllBytes(file))) continue;
                frames = new Sprite[info.frames];
                float fw = tex.width / (float)info.frames;
                for (int k = 0; k < info.frames; k++)
                    frames[k] = Sprite.Create(tex, new Rect(Mathf.Round(k * fw), 0f, Mathf.Round(fw), tex.height), new Vector2(0.5f, 0f), 100f * z);
                Cache[key] = frames;
                return frames;
            }
            return null;
        }

        /// <summary>Forget the pictures (the player changed the scale).</summary>
        public static void ClearCache()
        {
            Cache.Clear();
        }
    }
}
