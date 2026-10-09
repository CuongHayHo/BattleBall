using System;
using System.IO;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The weather and the time of day of the village (owner request 2026-10-09: sun, rain, light and dark, at random). Pictures laid over the world and under every
    /// button: the sunbeam of Nro (bg/light.png) when it is sunny, drifting clouds (bg/fog0.png) and two layers of falling streaks when it rains (with a flash of
    /// lightning now and then), a dark blue veil and twinkling stars at night. The sun or the rain and the hour change by themselves at random, softly (several seconds
    /// to fade); nothing here is sent to or kept by the server (each player has his own sky: PROPOSED). Switched off with {@code Enabled}.
    /// </summary>
    public sealed class BbWeather : MonoBehaviour
    {
        const string PrefKey = "bb_weather";

        /// <summary>The player's choice (on by default), kept between runs.</summary>
        public static bool Enabled
        {
            get { try { return PlayerPrefs.GetInt(PrefKey, 1) != 0; } catch (Exception) { return true; } }
            set { try { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); } catch (Exception) { } }
        }

        RectTransform _root;
        Image _veil, _sun, _flash;
        RawImage _clouds, _rainNear, _rainFar, _stars;
        Texture2D _rainTexture, _starTexture, _cloudTexture;
        Sprite _sunSprite;
        readonly System.Random _rng = new System.Random();

        // what the sky is and what it is becoming
        float _rain, _rainTarget;                 // 0 = dry, 1 = pouring
        float _night, _nightTarget;               // 0 = full day, 1 = night
        float _weatherLeft, _hourLeft;            // seconds until the next change
        float _lightning;                         // the flash that is fading (0..1)
        float _nextLightning = 12f;
        float _clock;
        bool _forced;

        public float Rain { get { return _rain; } }
        public float Night { get { return _night; } }

        /// <summary>Covers the area of {@code viewport} (the village) and lies just over it.</summary>
        public void Attach(RectTransform viewport)
        {
            if (_root != null || viewport == null || viewport.parent == null) return;
            var go = new GameObject("Weather", typeof(RectTransform));
            go.transform.SetParent(viewport.parent, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            _root.SetSiblingIndex(viewport.GetSiblingIndex() + 1);
            Build();
            _rainTarget = _rng.NextDouble() < 0.3 ? 0.6f + (float)_rng.NextDouble() * 0.4f : 0f;
            _nightTarget = _rng.NextDouble() < 0.35 ? 1f : 0f;
            _rain = _rainTarget;
            _night = _nightTarget;
            _weatherLeft = Range(40f, 120f);
            _hourLeft = Range(60f, 160f);
        }

        float Range(float a, float b)
        {
            return a + (float)_rng.NextDouble() * (b - a);
        }

        // ---------------------------------------------------------------------------------------------------------------- building

        RawImage NewRaw(string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_root, false);
            var raw = go.GetComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;
            raw.color = new Color(1f, 1f, 1f, 0f);
            raw.canvasRenderer.cullTransparentMesh = false;
            var rt = raw.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return raw;
        }

        Image NewImage(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.canvasRenderer.cullTransparentMesh = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        static Texture2D LoadPicture(string name)
        {
            try
            {
                string file = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "weather", name);
                if (!File.Exists(file)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
                return tex.LoadImage(File.ReadAllBytes(file)) ? tex : null;
            }
            catch (Exception) { return null; }
        }

        void Build()
        {
            // the veil: night and the grey of the rain (one picture, its colour and alpha follow the sky)
            var white = Texture2D.whiteTexture;
            var whiteSprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f));
            _veil = NewImage("Veil", whiteSprite);

            _cloudTexture = LoadPicture("fog0.png");
            if (_cloudTexture != null) _clouds = NewRaw("Clouds", _cloudTexture);
            if (_clouds != null)
            {
                _clouds.rectTransform.anchorMin = new Vector2(0f, 0.5f);          // the upper half of the view
                _clouds.rectTransform.offsetMin = _clouds.rectTransform.offsetMax = Vector2.zero;
                _clouds.rectTransform.anchorMax = Vector2.one;
            }

            _starTexture = MakeStars();
            _stars = NewRaw("Stars", _starTexture);
            _stars.rectTransform.anchorMin = new Vector2(0f, 0.45f);

            var light = LoadPicture("light.png");
            if (light != null)
            {
                _sunSprite = Sprite.Create(light, new Rect(0, 0, light.width, light.height), new Vector2(0f, 1f), 100f);
                _sun = NewImage("Sun", _sunSprite);
                var rt = _sun.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);                 // the sunbeam comes from the top left corner
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(760f, 700f);
                rt.anchoredPosition = new Vector2(-30f, 20f);
            }

            _rainTexture = MakeRain();
            _rainFar = NewRaw("RainFar", _rainTexture);
            _rainNear = NewRaw("RainNear", _rainTexture);
            _flash = NewImage("Lightning", whiteSprite);
        }

        /// <summary>A tile of slanted streaks (transparent around): it repeats and scrolls to make the falling rain.</summary>
        Texture2D MakeRain()
        {
            const int N = 256;
            var pixels = new Color32[N * N];
            var rnd = new System.Random(7);
            for (int s = 0; s < 70; s++)
            {
                int x = rnd.Next(N), y = rnd.Next(N), length = rnd.Next(16, 42);
                byte alpha = (byte)rnd.Next(90, 190);
                for (int i = 0; i < length; i++)
                {
                    int px = ((x - i / 4) % N + N) % N, py = ((y + i) % N + N) % N;       // slanted: one pixel to the left for four down
                    float fade = 1f - i / (float)length;
                    pixels[py * N + px] = new Color32(205, 225, 255, (byte)(alpha * fade));
                }
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        Texture2D MakeStars()
        {
            const int W = 512, H = 256;
            var pixels = new Color32[W * H];
            var rnd = new System.Random(11);
            for (int s = 0; s < 150; s++)
            {
                int x = rnd.Next(1, W - 1), y = rnd.Next(1, H - 1);
                byte a = (byte)rnd.Next(140, 255);
                pixels[y * W + x] = new Color32(255, 255, 235, a);
                if (rnd.Next(5) == 0)
                {
                    byte b = (byte)(a / 2);
                    pixels[y * W + x + 1] = new Color32(255, 255, 235, b);
                    pixels[y * W + x - 1] = new Color32(255, 255, 235, b);
                    pixels[(y + 1) * W + x] = new Color32(255, 255, 235, b);
                    pixels[(y - 1) * W + x] = new Color32(255, 255, 235, b);
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------------------------------------------------------------- the sky

        /// <summary>For the screenshot tool and tests: this sky at once (night 0..1, rain 0..1); the random changes stop.</summary>
        public void Force(float night, float rain)
        {
            _forced = true;
            _night = _nightTarget = night;
            _rain = _rainTarget = rain;
            Apply(0f);
        }

        void Update()
        {
            if (_root == null) return;
            float dt = Time.unscaledDeltaTime;
            if (!Enabled)
            {
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                return;
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            if (!_forced)
            {
                _weatherLeft -= dt;
                if (_weatherLeft <= 0f)
                {
                    // sun or rain, at random; the rain is light or heavy
                    bool rain = _rainTarget < 0.1f && _rng.NextDouble() < 0.45;
                    _rainTarget = rain ? Range(0.5f, 1f) : 0f;
                    _weatherLeft = rain ? Range(40f, 110f) : Range(60f, 180f);
                }
                _hourLeft -= dt;
                if (_hourLeft <= 0f)
                {
                    // the hour: day, evening or night, at random
                    double r = _rng.NextDouble();
                    _nightTarget = r < 0.45 ? 0f : r < 0.7 ? 0.45f : 1f;
                    _hourLeft = Range(60f, 200f);
                }
                _rain = Mathf.MoveTowards(_rain, _rainTarget, dt * 0.2f);
                _night = Mathf.MoveTowards(_night, _nightTarget, dt * 0.07f);
            }
            Apply(dt);
        }

        void Apply(float dt)
        {
            if (_root == null) return;
            _clock += dt;
            float day = 1f - _night;
            // the veil: the dark blue of the night, the grey of the rain
            Color night = new Color(0.02f, 0.04f, 0.15f);
            Color grey = new Color(0.22f, 0.27f, 0.34f);
            float nightAlpha = 0.7f * _night, greyAlpha = 0.22f * _rain * (0.4f + 0.6f * day);
            float a = nightAlpha + greyAlpha * (1f - nightAlpha);
            Color c = a > 0.001f ? (night * nightAlpha + grey * greyAlpha * (1f - nightAlpha)) / a : night;
            _veil.color = new Color(c.r, c.g, c.b, a);
            // the sun: only when it does not rain and it is light
            if (_sun != null) _sun.color = new Color(1f, 1f, 0.95f, Mathf.Clamp01((1f - _rain * 1.4f)) * day * (0.8f + 0.1f * Mathf.Sin(_clock * 0.4f)));
            // the clouds: a few white ones in the sun, many dark ones in the rain
            if (_clouds != null)
            {
                float cover = 0.18f + 0.7f * _rain;
                float shade = Mathf.Lerp(1f, 0.45f, _rain) * Mathf.Lerp(1f, 0.35f, _night);
                _clouds.color = new Color(shade, shade, Mathf.Min(1f, shade * 1.05f), cover);
                var uv = _clouds.uvRect;
                uv.width = 1.3f;
                uv.y = 1f;
                uv.height = -1f;                                            // upside down: the dense part of the picture is at the top, it thins out downwards
                uv.x = (_clock * 0.004f) % 1f;                              // they drift to the right slowly
                _clouds.uvRect = uv;
            }
            // the stars twinkle at night
            if (_stars != null) _stars.color = new Color(1f, 1f, 1f, Mathf.Clamp01((_night - 0.35f) * 1.6f) * (1f - _rain * 0.8f) * (0.8f + 0.2f * Mathf.Sin(_clock * 2.3f)));
            // the rain: two layers, the near one faster and bigger
            if (_rainFar != null)
            {
                _rainFar.color = new Color(1f, 1f, 1f, 0.55f * _rain);
                _rainNear.color = new Color(1f, 1f, 1f, 0.8f * _rain);
                var far = _rainFar.uvRect;
                far.width = 5f; far.height = 2.8f;
                far.y = (_clock * 1.4f) % 1f;
                far.x = (_clock * 0.35f) % 1f;
                _rainFar.uvRect = far;
                var near = _rainNear.uvRect;
                near.width = 3.2f; near.height = 1.8f;
                near.y = (_clock * 2.1f) % 1f;
                near.x = (_clock * 0.52f) % 1f;
                _rainNear.uvRect = near;
            }
            // the lightning of a heavy rain: a white flash now and then
            if (_rain > 0.6f)
            {
                _nextLightning -= dt;
                if (_nextLightning <= 0f) { _lightning = 1f; _nextLightning = Range(8f, 22f); }
            }
            if (_lightning > 0f)
            {
                _lightning = Mathf.Max(0f, _lightning - dt * 2.6f);
                float flicker = _lightning > 0.55f ? 1f : 0.5f + 0.5f * Mathf.Sin(_lightning * 40f);          // a quick flicker as it dies away
                _flash.color = new Color(0.92f, 0.95f, 1f, 0.5f * _lightning * flicker);
            }
            else _flash.color = new Color(1f, 1f, 1f, 0f);
        }
    }
}
