using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The quests of Ông Gôhan (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.32): the quest being done of the chain, then the daily quests, as rows with the title, what to do,
    /// the progress, the NPC to go to and the reward (gold, gems, skill cards, stones). A tap on a row makes it the quest the arrow of the village points to; a finished quest has a button that
    /// takes its reward. Like the panel of the elder it stands on the left and the player's panel goes to the right. The list comes from the server inside every PlayerDataSnapshot
    /// (PlayerDataSnapshot.quests); the server counts, decides and pays (ClaimQuestRequest). The hierarchy is built by BattleBall/Build Login Scene, the rows are made at run time.
    /// </summary>
    public sealed class BbQuestPanel : MonoBehaviour
    {
        public GameObject root;
        public RectTransform panelRect;
        public Image portrait;
        public Text headerText;
        public Button closeButton;
        public Text goldText, gemText, cardText;
        public Text titleText;
        public ScrollRect scroll;
        public RectTransform content;
        public Font font;
        public Sprite rowSprite, buttonSprite;
        public Text resultText;
        public Button claimAllButton;
        public Text claimAllLabel;
        public BbMenuUi menu;

        /// <summary>Raised when the player closes the panel (the X).</summary>
        public Action Closed;

        const float PanelWidth = 320f, RowH = 100f;
        static readonly Color RowIdle = new Color(0.906f, 0.875f, 0.824f), RowDone = new Color(0.72f, 0.92f, 0.62f), RowClaimed = new Color(0.78f, 0.75f, 0.70f), Tracked = new Color(1f, 0.95f, 0.55f);
        static readonly Color Good = new Color(0.12f, 0.56f, 0.12f), Bad = new Color(0.72f, 0.12f, 0.12f), Ink = new Color(0.18f, 0.10f, 0.04f), Blue = new Color(0.12f, 0.24f, 0.62f),
                              Muted = new Color(0.45f, 0.40f, 0.35f), Orange = new Color(0.72f, 0.36f, 0.04f);

        PlayerDataSnapshot _snapshot;
        readonly List<GameObject> _rows = new List<GameObject>();
        bool _open, _wired, _busy;
        float _x = -PanelWidth;
        ulong _pendingGold, _pendingGems, _pendingCards;
        bool _pending;

        public bool IsOpen { get { return _open; } }
        public float VisibleWidth { get { return Mathf.Clamp(PanelWidth + _x, 0f, PanelWidth); } }
        public string DebugResult { get { return resultText != null ? resultText.text : ""; } }
        public int DebugRows { get { return _rows.Count; } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            closeButton.onClick.AddListener(Close);
            claimAllButton.onClick.AddListener(() => Claim(""));
        }

        void Awake() { Wire(); }

        public void Open(PlayerDataSnapshot snapshot, bool immediate = false)
        {
            Wire();
            _snapshot = snapshot;
            resultText.text = "";
            _open = true;
            root.SetActive(true);
            if (immediate) { _x = 0f; Apply(); }
            var sp = BbCostumeAvatar.Of(9024);                              // Ông Gôhan
            portrait.sprite = sp;
            portrait.enabled = sp != null;
            titleText.text = "Nhiệm vụ";
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            Refresh();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (Closed != null) Closed();
        }

        void Update()
        {
            float target = _open ? 0f : -PanelWidth;
            if (!Mathf.Approximately(_x, target))
            {
                _x = Mathf.MoveTowards(_x, target, PanelWidth * 5f * Time.unscaledDeltaTime);
                Apply();
                if (!_open && Mathf.Approximately(_x, -PanelWidth)) root.SetActive(false);
            }
        }

        void Apply()
        {
            panelRect.anchoredPosition = new Vector2(_x, 0f);
            if (menu != null) menu.ApplyInsets();
        }

        public void OnSnapshot(PlayerDataSnapshot snapshot)
        {
            if (_pending && _snapshot != null) ReportDelta(snapshot);
            _snapshot = snapshot;
            if (_open) Refresh();
        }

        // ---------------------------------------------------------------------------------------------------------------- texts

        static string Money(ulong v) { return v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.'); }

        /// <summary>The skill cards the account holds (material 206).</summary>
        public static ulong CardsOf(PlayerDataSnapshot s)
        {
            if (s != null) foreach (var m in s.Materials) if (m.TemplateId == 206) return m.Quantity;
            return 0;
        }

        /// <summary>What a quest gives, on one line: gold, gems, then the items (the skill cards and the stones).</summary>
        public static string RewardText(QuestEntry q)
        {
            var sb = new StringBuilder();
            if (q.Gold > 0) sb.Append("+").Append(Money(q.Gold)).Append(" vàng");
            if (q.Gems > 0) sb.Append(sb.Length > 0 ? ", " : "").Append("+").Append(q.Gems).Append(" ngọc");
            foreach (var it in q.Items) sb.Append(sb.Length > 0 ? ", " : "").Append(it.Name).Append(" x").Append(it.Quantity);
            return sb.ToString();
        }

        /// <summary>The quests that are finished and not yet paid.</summary>
        public static int Claimable(PlayerDataSnapshot s)
        {
            int n = 0;
            if (s != null) foreach (var q in s.Quests) if (!q.Claimed && q.Progress >= q.Target) n++;
            return n;
        }

        string NpcName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var spec = menu != null && menu.village != null ? menu.village.NpcOfId(id) : null;
            return spec != null ? spec.Name : id;
        }

        // ---------------------------------------------------------------------------------------------------------------- rows

        void Refresh()
        {
            if (!_open || _snapshot == null) return;
            foreach (var r in _rows) Kill(r);
            _rows.Clear();
            int total = _snapshot.Quests.Count, ready = Claimable(_snapshot), done = 0;
            string tracked = menu != null ? menu.TrackedQuestId : null;
            foreach (var q in _snapshot.Quests)
            {
                if (q.Claimed || q.Progress >= q.Target) done++;
                AddRow(q, q.Id == tracked);
            }
            headerText.text = "Ông Gôhan\nNhiệm vụ\nXong " + done + "/" + total + (ready > 0 ? " (nhận " + ready + ")" : "");
            claimAllButton.interactable = ready > 0 && !_busy;
            claimAllLabel.text = ready > 0 ? "Nhận thưởng\n(" + ready + ")" : "Nhận thưởng";
            goldText.text = Money(_snapshot.Gold);
            gemText.text = Money(_snapshot.Gems);
            cardText.text = "Thẻ: " + Money(CardsOf(_snapshot));
        }

        void AddRow(QuestEntry q, bool tracked)
        {
            bool done = q.Progress >= q.Target;
            var go = NewRect("Quest_" + q.Id, content);
            go.gameObject.AddComponent<LayoutElement>().preferredHeight = RowH;
            var bg = go.gameObject.AddComponent<Image>();
            bg.sprite = rowSprite;
            bg.color = tracked ? Tracked : q.Claimed ? RowClaimed : done ? RowDone : RowIdle;
            var button = go.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.pressedColor = new Color(0.98f, 1f, 0.29f);
            button.colors = colors;
            string id = q.Id;
            button.onClick.AddListener(() => { if (menu != null) menu.TrackQuest(id); });

            float w = 316f;
            var title = NewText(go, q.Title, 16, q.Claimed ? Muted : Blue, TextAnchor.UpperLeft, true);
            Place(title.rectTransform, 6f, 3f, w - 96f, 22f);
            var tag = NewText(go, q.Kind == "DAILY" ? "Hằng ngày" : "Nhiệm vụ chính", 11, q.Kind == "DAILY" ? Orange : Good, TextAnchor.UpperRight, true);
            Place(tag.rectTransform, w - 96f, 5f, 90f, 16f);
            var desc = NewText(go, q.Description, 12, Ink, TextAnchor.UpperLeft, false);
            Place(desc.rectTransform, 6f, 24f, w - 12f, 32f);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.verticalOverflow = VerticalWrapMode.Truncate;
            desc.resizeTextForBestFit = true;
            desc.resizeTextMinSize = 9;
            desc.resizeTextMaxSize = 12;

            string where = done ? "Về gặp Ông Gôhan" : !string.IsNullOrEmpty(q.NpcId) ? "Đến " + NpcName(q.NpcId) : "Làm trong bảng nhân vật";
            string progress = q.Claimed ? "Đã nhận thưởng" : "Tiến độ " + q.Progress + "/" + q.Target + "  -  " + where;
            var line = NewText(go, progress, 12, q.Claimed ? Muted : done ? Good : Ink, TextAnchor.MiddleLeft, true);
            Place(line.rectTransform, 6f, 56f, w - 12f, 16f);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            line.resizeTextForBestFit = true;
            line.resizeTextMinSize = 9;
            line.resizeTextMaxSize = 12;

            var reward = NewText(go, "Thưởng: " + RewardText(q), 12, q.Claimed ? Muted : Orange, TextAnchor.UpperLeft, false);
            Place(reward.rectTransform, 6f, 74f, w - (done && !q.Claimed ? 90f : 12f), 26f);
            reward.horizontalOverflow = HorizontalWrapMode.Wrap;
            reward.verticalOverflow = VerticalWrapMode.Truncate;
            reward.resizeTextForBestFit = true;
            reward.resizeTextMinSize = 9;
            reward.resizeTextMaxSize = 12;

            if (done && !q.Claimed)
            {
                var br = NewRect("Claim", go);
                Place(br, w - 80f, 60f, 74f, 36f);
                var bi = br.gameObject.AddComponent<Image>();
                bi.sprite = buttonSprite;
                bi.type = Image.Type.Sliced;
                var b = br.gameObject.AddComponent<Button>();
                b.targetGraphic = bi;
                b.onClick.AddListener(() => Claim(id));
                var bt = NewText(br, "Nhận", 15, Ink, TextAnchor.MiddleCenter, true);
                Stretch(bt.rectTransform, 2f, 2f);
            }
            _rows.Add(go.gameObject);
        }

        // ---------------------------------------------------------------------------------------------------------------- claim

        /// <summary>Takes the reward of a finished quest (an empty id: of every finished one).</summary>
        public async void Claim(string questId)
        {
            if (_busy || !BbSession.IsActive) return;
            _busy = true;
            _pending = true;
            _pendingGold = _snapshot != null ? _snapshot.Gold : 0;
            _pendingGems = _snapshot != null ? _snapshot.Gems : 0;
            _pendingCards = CardsOf(_snapshot);
            Refresh();
            try
            {
                var result = await BbSession.Client.ClaimQuestAsync(questId);
                if (!result.Ok)
                {
                    _pending = false;
                    resultText.color = Bad;
                    resultText.text = result.Error == ActionError.InvalidArgument ? "Chưa có nhiệm vụ nào xong để nhận" : "Không nhận được: " + result.Message;
                }
            }
            catch (Exception e)
            {
                _pending = false;
                resultText.color = Bad;
                resultText.text = "Không gửi được lệnh: " + e.Message;
            }
            _busy = false;
            if (_open) Refresh();
        }

        /// <summary>The snapshot after a claim: what the account got is worked out from the difference of the wallet and of the cards (the chain may have paid more than one quest).</summary>
        void ReportDelta(PlayerDataSnapshot now)
        {
            if (now.Gold == _pendingGold && now.Gems == _pendingGems && CardsOf(now) == _pendingCards) return;     // not the snapshot of the claim yet
            _pending = false;
            var sb = new StringBuilder("Nhận thưởng thành công!\n");
            var parts = new List<string>();
            if (now.Gold > _pendingGold) parts.Add("+" + Money(now.Gold - _pendingGold) + " vàng");
            if (now.Gems > _pendingGems) parts.Add("+" + (now.Gems - _pendingGems) + " ngọc");
            ulong cards = CardsOf(now);
            if (cards > _pendingCards) parts.Add("+" + (cards - _pendingCards) + " thẻ");
            resultText.color = Good;
            resultText.text = sb.Append(string.Join(", ", parts.ToArray())).ToString();
        }

        // ---------------------------------------------------------------------------------------------------------------- building blocks

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        Text NewText(Transform parent, string text, int size, Color color, TextAnchor align, bool bold)
        {
            var r = NewRect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform r, float padX, float padY)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
