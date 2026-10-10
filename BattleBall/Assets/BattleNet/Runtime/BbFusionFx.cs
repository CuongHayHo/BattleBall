using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The fusion (hợp thể) as the Nro client shows it (owner request 2026-10-10: "đối chiếu hiệu ứng hợp thể của src gốc của Teamobi và học theo"). Teamobi
    /// (Pet.fusion / unFusion / fusionEffect) sends message 125 (type, character) to the map; Char.setFusion then:
    ///  - type 6 (Potara) adds Effect(38) at the feet + 12 px (4 and 5, the dance, Effect(34)); the player's own character also closes the panel and locks the keys;
    ///  - for tFusion &lt; 100 (it counts twice a frame: about 1.7 s) the character is not painted (Char.isPaint) and the whole screen is painted white on every third
    ///    tick (GameScr.paint: gameTick % 3 == 0);
    ///  - splitting is the same message with type 0: no effect, but the same hidden character and the same blinking, and the partner is on the map again at once;
    ///  - while it lasts the fused character (isNhapThe) gets ServerEffect 114 every 25 ticks.
    /// </summary>
    public sealed class BbFusionFx : MonoBehaviour
    {
        public BbVillageView village;
        public BbMenuUi menu;

        const float Seconds = 100f / 2f / BbSkillPoses.FramesPerSecond;          // 100 ticks, counted twice a frame
        const float AuraEvery = 25f / BbSkillPoses.FramesPerSecond;               // isNhapThe: ServerEffect 114 every 25 ticks
        const int PotaraEffect = 38;                 // type 6, Potara (ConstPlayer.HOP_THE_PORATA)
        const int DanceEffect = 34;                  // type 4, Lưỡng long nhất thể (ConstPlayer.LUONG_LONG_NHAT_THE); 5 plays the same

        /// <summary>The kinds of fusion of the Nro client that have an effect of their own (message 125); splitting has none.</summary>
        public enum Kind { Split, Potara, Dance }

        Image _white;
        BbSkillFx _fx;
        ulong _main;                  // the character being merged or split (hidden while it lasts)
        float _start = float.MinValue;
        ulong _fusedMain;             // the fused character that wears the aura (0 = no fusion)
        float _nextAura;

        public bool Running { get { return _start != float.MinValue; } }

        /// <summary>The screen is white on this tick (for the play run that photographs it).</summary>
        public bool WhiteNow { get { return _white != null && _white.enabled; } }

        /// <summary>The fused character of the account (0 when there is no fusion): it wears the aura of the merged form.</summary>
        public void SetFused(ulong mainCharacter)
        {
            _fusedMain = mainCharacter;
        }

        /// <summary>Merges ({@code fuse}) or splits the character: it disappears under the white blinking, the Potara effect rises where it stood, the player cannot act.</summary>
        public void Play(bool fuse, ulong mainCharacter)
        {
            Play(fuse ? Kind.Potara : Kind.Split, mainCharacter);
        }

        /// <summary>
        /// The same with the kind of fusion given: Potara (the earring, the only fusion of the game design) or Dance (Nro type 4, which has Effect(34); the design has no
        /// fusion dance, so nothing in the game starts it yet).
        /// </summary>
        public void Play(Kind kind, ulong mainCharacter)
        {
            if (village == null) return;
            if (_white == null) Build();
            End();                                                               // a second use while it runs restarts it
            _main = mainCharacter;
            _start = Time.unscaledTime;
            village.SetHidden(_main, true);
            village.SetLocked(_main, true);
            village.CancelApproach();
            if (menu != null) menu.SetPanelOpen(false, true, false);              // Char.setFusion: GameCanvas.panel.hideNow()
            if (kind == Kind.Split) return;
            int effect = kind == Kind.Dance ? DanceEffect : PotaraEffect;
            ulong id;
            Vector2 feet;
            int facing;
            for (int i = 0; i < village.PartyCount; i++)
            {
                if (!village.PartyMember(i, out id, out feet, out facing) || id != _main) continue;
                Fx().PlayData(effect, feet + new Vector2(0f, -12f * village.UiScale), 1);      // Effect(38 or 34, cx, cy + 12): the origin of the frames
                break;
            }
        }

        BbSkillFx Fx()
        {
            if (_fx != null) return _fx;
            _fx = village.GetComponent<BbSkillFx>();
            if (_fx == null) _fx = village.gameObject.AddComponent<BbSkillFx>();
            _fx.village = village;
            return _fx;
        }

        void Build()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var go = new GameObject("White", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            _white = go.GetComponent<Image>();
            _white.color = Color.white;
            _white.raycastTarget = false;
            _white.canvasRenderer.cullTransparentMesh = false;
            var r = _white.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            _white.enabled = false;
            transform.SetAsFirstSibling();                                       // under the buttons, over the village
        }

        void End()
        {
            if (_start == float.MinValue) return;
            _start = float.MinValue;
            if (_white != null) _white.enabled = false;
            if (village == null) return;
            village.SetHidden(_main, false);
            village.SetLocked(_main, false);
        }

        void Update()
        {
            if (village == null) return;
            float now = Time.unscaledTime;
            if (_start != float.MinValue)
            {
                if (now - _start >= Seconds) { End(); return; }
                int tick = Mathf.FloorToInt((now - _start) * BbSkillPoses.FramesPerSecond);
                if (_white != null) _white.enabled = tick % 3 == 0;               // GameScr.paint: white on every third tick
                return;
            }
            if (_fusedMain == 0 || now < _nextAura) return;
            _nextAura = now + AuraEvery;
            ulong id;
            Vector2 feet;
            int facing;
            for (int i = 0; i < village.PartyCount; i++)
            {
                if (!village.PartyMember(i, out id, out feet, out facing) || id != _fusedMain) continue;
                Fx().PlayEffectAt(114, feet + new Vector2(0f, 30f * village.UiScale), 1);          // addServerEffect(114, this, 1) while isNhapThe
                break;
            }
        }
    }
}
