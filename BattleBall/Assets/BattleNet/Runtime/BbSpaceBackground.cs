using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// Space background of the start screens: the star picture scrolls slowly to the left and its tiles follow each other end to end (every second
    /// tile is mirrored, so the seams match and the loop has no visible join), and a few small planets (two sprites, each used twice at different
    /// sizes) spin slowly and drift diagonally, re-entering from the opposite side. All sizes are relative to the height of this rectangle, so it
    /// looks the same in landscape and portrait. Put it on a full-screen RectTransform; <see cref="Build"/> is safe to call again.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BbSpaceBackground : MonoBehaviour
    {
        public Sprite background;
        public Sprite planetA, planetB;
        [Tooltip("Scroll speed of the stars, in rectangle heights per second")]
        public float scrollSpeed = 0.012f;

        struct Planet
        {
            public RectTransform rect;
            public float diameter;          // in rectangle heights
            public Vector2 start;           // start position as a fraction of width and height
            public Vector2 velocity;        // rectangle heights per second
            public float spinDegPerSec;
            public float startAngle;
        }

        readonly List<RectTransform> _tiles = new List<RectTransform>();
        readonly List<Planet> _planets = new List<Planet>();
        RectTransform _rt;

        void Awake() { Build(); }
        void Update() { Apply(Time.unscaledTime); }

        /// <summary>Creates the child images (tiles and planets); an earlier set is removed first.</summary>
        public void Build()
        {
            _rt = GetComponent<RectTransform>();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            _tiles.Clear();
            _planets.Clear();
            if (background == null) return;

            // parameters: bigger planets are a little faster (they are "closer"); all drift down and to the right, a few degrees per second of spin
            AddPlanet(planetA, 0.11f, new Vector2(-0.34f, 0.22f), new Vector2(0.014f, -0.009f), 4f, 20f);
            AddPlanet(planetB, 0.17f, new Vector2(0.30f, -0.12f), new Vector2(0.018f, -0.011f), -3f, 80f);
            AddPlanet(planetA, 0.07f, new Vector2(0.05f, 0.34f), new Vector2(0.010f, -0.006f), -5f, 200f);
            AddPlanet(planetB, 0.10f, new Vector2(-0.12f, -0.30f), new Vector2(0.012f, -0.008f), 3.5f, 300f);
            Apply(0f);
        }

        void AddPlanet(Sprite sprite, float diameter, Vector2 start, Vector2 velocity, float spin, float angle)
        {
            if (sprite == null) return;
            var go = new GameObject("Planet", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            _planets.Add(new Planet { rect = rect, diameter = diameter, start = start, velocity = velocity, spinDegPerSec = spin, startAngle = angle });
        }

        RectTransform NewTile()
        {
            var go = new GameObject("StarTile", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling();           // the planets stay in front of the stars
            var img = go.GetComponent<Image>();
            img.sprite = background;
            img.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        /// <summary>Puts everything where it is at time <paramref name="t"/> (seconds). Called every frame in play mode.</summary>
        public void Apply(float t)
        {
            if (_rt == null) _rt = GetComponent<RectTransform>();
            if (background == null) return;
            Vector2 size = _rt.rect.size;
            if (size.x < 1f || size.y < 1f) return;

            float h = size.y;
            float aspect = background.rect.width / background.rect.height;
            float tileW = h * aspect;
            int needed = Mathf.CeilToInt(size.x / tileW) + 3;      // covers the width plus the part that has scrolled out
            if (needed % 2 == 1) needed++;
            while (_tiles.Count < needed) _tiles.Add(NewTile());

            float period = 2f * tileW;                              // one normal tile followed by one mirrored tile
            float offset = Mathf.Repeat(t * scrollSpeed * h, period);
            for (int i = 0; i < _tiles.Count; i++)
            {
                var r = _tiles[i];
                r.sizeDelta = new Vector2(tileW + 1f, h);           // 1 px overlap hides hairline gaps between tiles
                r.anchoredPosition = new Vector2(-size.x * 0.5f + tileW * (i + 0.5f) - offset, 0f);
                r.localScale = new Vector3(i % 2 == 0 ? 1f : -1f, 1f, 1f);
            }

            foreach (var p in _planets)
            {
                float d = p.diameter * h;
                float marginX = d, marginY = d;
                float spanX = size.x + 2f * marginX, spanY = size.y + 2f * marginY;
                float x = p.start.x * size.x + p.velocity.x * h * t;
                float y = p.start.y * size.y + p.velocity.y * h * t;
                x = Mathf.Repeat(x + size.x * 0.5f + marginX, spanX) - size.x * 0.5f - marginX;
                y = Mathf.Repeat(y + size.y * 0.5f + marginY, spanY) - size.y * 0.5f - marginY;
                p.rect.sizeDelta = new Vector2(d, d);
                p.rect.anchoredPosition = new Vector2(x, y);
                p.rect.localRotation = Quaternion.Euler(0f, 0f, p.startAngle + p.spinDegPerSec * t);
            }
        }
    }
}
