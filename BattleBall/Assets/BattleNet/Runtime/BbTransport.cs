using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The screen between the village and a dungeon (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25), after the Nro TransportScr of type 1: the picture
    /// <c>taungam</c> (the CAPSULE 82 ship) starts at the middle of the left edge, flips to face right and flies to the right while streaks of light run to the left, faster
    /// and faster. The background is the one of the login screen (BbSpaceBackground) instead of the plain blue of Nro. The server answer arrives while the ship flies:
    /// <see cref="Complete"/> lets it leave the screen at once (it never leaves before it has flown in), <see cref="Fail"/> lets it go back with a message. The hierarchy is made by
    /// BattleBall/Build Login Scene; it covers everything and swallows every touch.
    /// </summary>
    public sealed class BbTransport : MonoBehaviour
    {
        public GameObject root;
        public RectTransform area;                 // the full screen
        public BbSpaceBackground space;
        public Image ship;
        public Sprite streakSprite;
        public Text caption;
        public CanvasGroup fade;

        // Nro (TransportScr, in a screen of about 320 px height, 30 ticks a second): the ship moves 2 px a tick, the streaks 5 px a tick growing to 25 (to 80 when it leaves)
        const float FlyInSeconds = 1.6f;           // from the left edge to the middle
        const float MinHoldSeconds = 0.9f;         // the ship rests (hovers) at least this long at the middle, so that the arrival is never a blink
        const float LeaveAcceleration = 2600f;     // canvas px per second squared
        const float FadeSeconds = 0.35f;
        const float TimeoutSeconds = 20f;          // the server has not answered: go back
        const int Streaks = 20;

        enum Phase { Off, FlyIn, Hold, Leave, FadeOut }

        sealed class Streak
        {
            public RectTransform rect;
            public bool near;
        }

        readonly List<Streak> _streaks = new List<Streak>();
        Phase _phase;
        float _phaseStart, _speed, _shipX, _shipV, _width, _height;
        bool _done, _failed;
        string _failMessage;
        Action _reveal, _finished;
        Action<string> _failedCallback;
        bool _built;

        public bool IsPlaying { get { return _phase != Phase.Off; } }

        /// <summary>Starts the flight. <paramref name="text"/> is written under the ship; <paramref name="failed"/> gets the reason when <see cref="Fail"/> is called or the server never answers.</summary>
        public void Begin(string text, Action<string> failed)
        {
            Build();
            _done = _failed = false;
            _failMessage = null;
            _reveal = null;
            _finished = null;
            _failedCallback = failed;
            caption.text = text;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            fade.alpha = 1f;
            _speed = 140f;
            _phase = Phase.FlyIn;
            _phaseStart = Time.unscaledTime;
            Measure();
            _shipX = 0f;                           // the centre of the ship on the left edge
            _shipV = 0f;
            Place(0f);
        }

        /// <summary>The server said yes: the ship leaves to the right when it has flown in. <paramref name="reveal"/> runs when the ship has left (what lies under the screen is shown),
        /// <paramref name="finished"/> when the screen has faded away.</summary>
        public void Complete(Action reveal, Action finished = null)
        {
            _done = true;
            _reveal = reveal;
            _finished = finished;
        }

        /// <summary>The server said no (or nothing): the screen goes away after the ship has flown in; the callback of <see cref="Begin"/> gets the message.</summary>
        public void Fail(string message)
        {
            _failed = true;
            _failMessage = message;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            for (int i = 0; i < Streaks * 2; i++)
            {
                var go = new GameObject("Streak", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(area, false);
                var img = go.GetComponent<Image>();
                img.sprite = streakSprite;
                img.raycastTarget = false;
                bool near = i >= Streaks;
                img.color = near ? new Color(0.45f, 0.99f, 1f, 0.85f) : new Color(0.66f, 1f, 1f, 0.55f);       // Nro 7536127 (near) and 11140863 (far)
                var r = go.GetComponent<RectTransform>();
                r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
                r.pivot = new Vector2(0f, 0.5f);
                _streaks.Add(new Streak { rect = r, near = near });
            }
            ship.transform.SetAsLastSibling();                       // the ship flies over the streaks
            if (caption != null) caption.transform.SetAsLastSibling();
        }

        void Measure()
        {
            var size = area.rect.size;
            _width = Mathf.Max(200f, size.x);
            _height = Mathf.Max(120f, size.y);
            float shipH = _height * 0.27f;                           // Nro: 79 of about 320 px
            float aspect = ship.sprite != null ? ship.sprite.rect.width / ship.sprite.rect.height : 1.82f;
            ship.rectTransform.sizeDelta = new Vector2(shipH * aspect, shipH);
            ship.rectTransform.localScale = new Vector3(-1f, 1f, 1f);        // the picture faces left; Nro draws it mirrored
            ship.rectTransform.anchorMin = ship.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            ship.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < _streaks.Count; i++)
            {
                var s = _streaks[i];
                float k = _height / 320f;
                s.rect.sizeDelta = s.near ? new Vector2(18f * k, 3f * k) : new Vector2(10f * k, 2f * k);
                int n = i % Streaks;
                s.rect.anchoredPosition = new Vector2(UnityEngine.Random.Range(0f, _width), n * (_height / Streaks));
            }
        }

        void Place(float bob)
        {
            ship.rectTransform.anchoredPosition = new Vector2(_shipX, bob);
            if (caption != null) caption.rectTransform.anchoredPosition = new Vector2(_width * 0.5f, 44f);
        }

        void Update()
        {
            if (_phase == Phase.Off) return;
            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime - _phaseStart;
            Measure2();
            switch (_phase)
            {
                case Phase.FlyIn:
                {
                    float k = Mathf.Clamp01(t / FlyInSeconds);
                    _shipX = Mathf.Lerp(0f, _width * 0.5f, Mathf.Sin(k * Mathf.PI * 0.5f));      // slows down as it reaches the middle
                    Place(0f);
                    if (k >= 1f) Go(Phase.Hold);
                    break;
                }
                case Phase.Hold:
                {
                    Place(Mathf.Sin(t * 2.4f) * _height * 0.012f);
                    if (_failed && t >= MinHoldSeconds) { Go(Phase.FadeOut); }
                    else if (_done && t >= MinHoldSeconds) { Go(Phase.Leave); _shipV = 260f; }
                    else if (t >= TimeoutSeconds) { Fail("Hết thời gian chờ máy chủ"); }
                    break;
                }
                case Phase.Leave:
                {
                    _shipV += LeaveAcceleration * dt;
                    _shipX += _shipV * dt;
                    _speed = Mathf.Min(_speed + 900f * dt, 4200f);
                    Place(0f);
                    if (_shipX - ship.rectTransform.sizeDelta.x * 0.5f > _width)
                    {
                        if (_reveal != null) { var r = _reveal; _reveal = null; r(); }
                        Go(Phase.FadeOut);
                    }
                    break;
                }
                case Phase.FadeOut:
                {
                    fade.alpha = 1f - Mathf.Clamp01(t / FadeSeconds);
                    if (t >= FadeSeconds) Finish();
                    break;
                }
            }
            if (_phase != Phase.Off) MoveStreaks(dt);
        }

        void Measure2()
        {
            var size = area.rect.size;
            if (Mathf.Abs(size.x - _width) > 1f || Mathf.Abs(size.y - _height) > 1f) Measure();      // the window changed
        }

        void Go(Phase p)
        {
            _phase = p;
            _phaseStart = Time.unscaledTime;
        }

        void MoveStreaks(float dt)
        {
            if (_phase != Phase.Leave) _speed = Mathf.Min(_speed + 220f * dt, 1700f);            // Nro: +1 px a tick every 3 ticks up to 25
            foreach (var s in _streaks)
            {
                var p = s.rect.anchoredPosition;
                p.x -= (s.near ? _speed : _speed * 0.5f) * dt;
                if (p.x < -60f) p.x = _width + UnityEngine.Random.Range(0f, 80f);
                s.rect.anchoredPosition = p;
            }
        }

        void Finish()
        {
            _phase = Phase.Off;
            root.SetActive(false);
            if (_failed)
            {
                var f = _failedCallback;
                _failedCallback = null;
                if (f != null) f(_failMessage);
                return;
            }
            var done = _finished;
            _finished = null;
            if (done != null) done();
        }
    }
}
