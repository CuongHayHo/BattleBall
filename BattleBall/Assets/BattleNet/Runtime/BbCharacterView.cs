using System.Collections.Generic;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// Draws a costume of the Nro mannequin with plain UI Images (head, legs, body) inside a RectTransform, so it needs no camera, render texture or 2D light and
    /// works in every canvas. The placement is the one of NroMannequin.PlaceSlot (pose table offsets plus the frame offsets, in Nro pixels) at a fixed UI scale.
    /// Costume parts and sprites come from NroCostumeCatalog and NroSpriteResolver (the sprites are the downloaded assets on a device).
    /// The origin is the bottom centre of this rectangle (the feet).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BbCharacterView : MonoBehaviour
    {
        [Tooltip("UI pixels per Nro pixel")]
        public float uiScale = 2f;
        [Tooltip("Seconds between the two breathing frames at animSpeed 1 (owner request 2026-10-08: breathing twice as fast, was 0.6)")]
        public float breathSeconds = 0.3f;
        [Tooltip("Speed of the standing and running frames: 1 = base speed (breathing every 0.3 s, a run frame every 0.08 s). The village sets it per character: 2 for the main one (4 while it runs), 1 for a standing AI, a fixed run pace for a running AI.")]
        public float animSpeed = 1f;
        [Tooltip("Speed of the head frames of costumes with a moving head: 1 = the Nro client's speed, 2 = twice as fast (owner request 2026-10-08)")]
        public float headFrameSpeed = 2f;

        // default body and legs by planet (Nro CreateCharScr.defaultBody and defaultLeg: Trái Đất, Namếc, Xayda); head-only costumes (the avatars) wear them
        static readonly int[] DefaultBody = { 157, 149, 151 };
        static readonly int[] DefaultLeg = { 158, 150, 152 };

        Image _head, _leg, _body;
        NroPartData _headPart, _bodyPart, _legPart;
        NroPartData[] _headCycle;                    // costumes with a moving head (fusion forms, some masks): the head parts in turn, as NroMannequin.headCycle
        int _headFrame;
        float _headClock;
        const float TicksPerSecond = 25f;            // NroMannequin: the Nro client changes the head frame every 4 game ticks of 25 per second
        const int TicksPerHeadFrame = 4;
        float _timer;
        bool _breath;

        public bool HasCostume { get; private set; }

        // ---- the aura of the account (owner decision 2026-10-08): drawn behind and in front of the character as Nro does (Char.paintAura*). Nro draws it only while the
        // character stands. The village sets this to true for the AI characters and leaves it false for the main one, which keeps its aura while it runs (owner request 2026-10-08).
        [Tooltip("true: the aura is drawn only while the character stands (the Nro behaviour); false: in every pose")]
        public bool auraOnlyWhenStanding;
        BbAuraRender _aura;

        public int AuraId { get { return _aura != null ? _aura.AuraId : 0; } }
        /// <summary>Height of the tallest frame of the aura in Nro pixels (0 without one): the popup sizes the preview by it.</summary>
        public float AuraNroHeight { get { return _aura != null ? _aura.NroHeight : 0f; } }

        public void SetAura(int auraId, IList<AuraLayer> layers)
        {
            if (_aura == null)
            {
                if (auraId <= 0) return;
                _aura = new BbAuraRender(transform);
            }
            if (auraId == _aura.AuraId && auraId > 0) return;
            _aura.uiScale = uiScale;
            _aura.Set(auraId, layers);
            _aura.Reorder();
        }

        /// <summary>The aura follows the scale of the view (a preview changes it); call after changing uiScale.</summary>
        public void RefreshAura()
        {
            if (_aura == null) return;
            _aura.uiScale = uiScale;
            _aura.Tick(0f);
        }

        /// <summary>Height of the top of the head above the feet in UI pixels (the Nro HP bar offset of the costume): names are drawn above it.</summary>
        public float OverheadHeight { get; private set; }

        public enum PoseState { Stand, Run, Jump, Fly, Fall }

        PoseState _state = PoseState.Stand;
        int _runFrame;

        // ---- a short action over the walking state (training, docs 15.12): a punch, a gong, a blast, a hit taken; the poses of the Nro table in turn
        int[] _action;
        float _actionTime, _actionLength;
        int _actionIndex = -1;

        /// <summary>Shows these poses of the Nro table one after the other over this many seconds, then goes back to the state (stand, run...).</summary>
        public void PlayAction(int[] poses, float seconds)
        {
            if (poses == null || poses.Length == 0 || seconds <= 0f) return;
            _action = poses;
            _actionTime = 0f;
            _actionLength = seconds;
            _actionIndex = -1;
            if (HasCostume) Place();
        }

        public bool Acting { get { return _action != null; } }

        /// <summary>Standing (breathing), running (the five Run poses of the Nro table in turn) or in the air; the map uses it for the characters that walk.</summary>
        public void SetState(PoseState state)
        {
            if (state == _state) return;
            _state = state;
            _timer = 0f;                              // the run cycle goes on where it stopped: a short run shows different steps, not always the first
            if (HasCostume) Place();
        }

        void EnsureImages()
        {
            if (_head != null) return;
            // drawing order (back to front): head, legs, body (the sorting orders 2, 3, 4 of NroMannequin)
            _head = NewSlot("Head");
            _leg = NewSlot("Leg");
            _body = NewSlot("Body");
        }

        Image NewSlot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0f, 1f);
            return img;
        }

        /// <summary>Shows the costume with this id; false when it is unknown (nothing is drawn).</summary>
        public bool Show(int costumeId)
        {
            EnsureImages();
            if (_aura != null) _aura.Reorder();                      // the slots made just now must lie between the two layers of the aura
            HasCostume = false;
            var catalog = NroCostumeCatalog.Instance;
            var entry = catalog != null ? catalog.GetCostume(costumeId) : null;
            if (entry == null)
            {
                _headPart = _bodyPart = _legPart = null;
                _headCycle = null;
                OverheadHeight = 0f;
                Place();
                return false;
            }
            int zoom = BbAssetCache.Scale;
            int planet = entry.gender >= 0 && entry.gender <= 2 ? entry.gender : 0;
            _headCycle = null;
            if (entry.HeadCyclePartIds != null && entry.HeadCyclePartIds.Length > 1)
            {
                _headCycle = new NroPartData[entry.HeadCyclePartIds.Length];
                for (int i = 0; i < _headCycle.Length; i++) _headCycle[i] = catalog.GetPart(entry.HeadCyclePartIds[i], zoom);
                _headPart = _headCycle[0];
            }
            else
            {
                _headPart = entry.HeadCyclePartIds != null && entry.HeadCyclePartIds.Length == 1
                    ? catalog.GetPart(entry.HeadCyclePartIds[0], zoom)
                    : (entry.HeadPartId >= 0 ? catalog.GetPart(entry.HeadPartId, zoom) : null);
            }
            _headFrame = 0;
            _headClock = 0f;
            OverheadHeight = (entry.HpBarOffsetY > 0 ? entry.HpBarOffsetY : 49) * uiScale;
            _bodyPart = catalog.GetPart(entry.BodyPartId >= 0 ? entry.BodyPartId : DefaultBody[planet], zoom);
            _legPart = catalog.GetPart(entry.LegPartId >= 0 ? entry.LegPartId : DefaultLeg[planet], zoom);
            _breath = false;
            _timer = 0f;
            HasCostume = true;
            Place();
            if (_aura != null) { _aura.Reorder(); RefreshAura(); }
            return true;
        }

        void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>Advances the breathing, the run frames and the head frames by this many seconds (public so a test can drive it).</summary>
        public void Tick(float dt)
        {
            if (_aura != null)
            {
                _aura.uiScale = uiScale;
                _aura.SetVisible(!auraOnlyWhenStanding || _state == PoseState.Stand);
                _aura.Tick(dt);
            }
            if (!HasCostume) return;
            if (_headCycle != null)
            {
                _headClock += dt * TicksPerSecond * headFrameSpeed;
                int frame = ((int)_headClock / TicksPerHeadFrame) % _headCycle.Length;
                if (frame != _headFrame)
                {
                    _headFrame = frame;
                    _headPart = _headCycle[frame] != null ? _headCycle[frame] : _headCycle[0];
                    Place();
                }
            }
            if (_action != null)
            {
                _actionTime += dt;
                if (_actionTime >= _actionLength) { _action = null; Place(); }
                else if (Mathf.Min(_action.Length - 1, (int)(_actionTime / _actionLength * _action.Length)) != _actionIndex) Place();
            }
            _timer += dt * animSpeed;
            float step = _state == PoseState.Run ? 0.08f : breathSeconds;
            if (_state != PoseState.Jump && _state != PoseState.Fall && _timer >= step)
            {
                _timer = 0f;
                _breath = !_breath;
                _runFrame = (_runFrame + 1) % 5;
                Place();
            }
        }

        void Place()
        {
            int cf = _state == PoseState.Run ? (int)NroPose.Run_1 + _runFrame
                   : _state == PoseState.Jump ? (int)NroPose.Jump_Up
                   : _state == PoseState.Fly ? (int)NroPose.Fly_Horizontal
                   : _state == PoseState.Fall ? (int)NroPose.Fall_Down
                   : _breath ? (int)NroPose.Stand_2_Breathing : (int)NroPose.Stand_1;
            if (_action != null)
            {
                _actionIndex = Mathf.Min(_action.Length - 1, (int)(_actionTime / _actionLength * _action.Length));
                cf = _action[_actionIndex];
            }
            // flying has one pose in the Nro table, so the parts swing like the idle breathing does (head and body one Nro pixel up on every other beat)
            int bob = _state == PoseState.Fly && _breath ? 1 : 0;
            PlaceSlot(_head, _headPart, NroSlot.Head, cf, bob);
            PlaceSlot(_leg, _legPart, NroSlot.Leg, cf, 0);
            PlaceSlot(_body, _bodyPart, NroSlot.Body, cf, bob);
        }

        void PlaceSlot(Image img, NroPartData part, NroSlot slot, int cf, int extraDy)
        {
            NroPartFrame f;
            if (part == null || !part.TryGetFrame(NroPoseTable.Sub(cf, slot), out f) || f.sprite == null)
            {
                img.enabled = false;
                return;
            }
            img.enabled = true;
            img.sprite = f.sprite;
            // Nro pixels (Y up): the top-left corner of the sprite sits at (px, py); the sprite is drawn at the zoom of its own atlas, so divide by it
            float px = NroPoseTable.Dx(cf, slot) + f.dx;
            float py = NroPoseTable.Dy(cf, slot) + extraDy - f.dy;
            float zoom = f.sprite.pixelsPerUnit / 100f;
            var r = img.rectTransform;
            r.sizeDelta = f.sprite.rect.size * (uiScale / zoom);
            // smaller than the texture (x3 or x4 drawn at a lower scale): filter smoothly, point filtering would shimmer; at 1:1 or larger keep the crisp Nro look
            f.sprite.texture.filterMode = uiScale / zoom < 1f ? FilterMode.Bilinear : FilterMode.Point;
            r.anchoredPosition = new Vector2(px * uiScale, py * uiScale);
        }
    }
}
