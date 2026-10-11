using System;
using System.Collections.Generic;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The quests of Ông Gôhan (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.32). Talking to him opens his dialog in the middle of the screen (the quest being done,
    /// a button that takes every finished reward, a button for the list of quests on the left). The server counts the quests and sends them inside every snapshot; this part of the menu
    /// shows a small tracker at the top of the screen, a bouncing arrow over the NPC the tracked quest needs (or, when that NPC is off the screen, at the edge towards it) and a toast when
    /// a quest is finished. The tracked quest is the one the player taps in the list, else the first finished one (go back to Gôhan), else the first one not finished.
    /// </summary>
    public sealed partial class BbMenuUi
    {
        [Header("Quests of Ông Gôhan")]
        public BbQuestPanel quest;               // the list of quests; stands where the shop stands
        public Image questTrackerBg;             // the small box at the top of the screen with the tracked quest
        public Text questTrackerText;

        string _trackedQuest;                    // chosen by a tap in the list ("" or null: automatic)
        readonly Dictionary<string, bool> _questDone = new Dictionary<string, bool>();
        bool _questSeen;
        RectTransform _guide;                    // the bouncing arrow
        Image _guideImage;
        Text _guideLabel;
        static Sprite _guideSprite;

        /// <summary>The quest the arrow and the tracker follow now (null when none is left).</summary>
        public QuestEntry ActiveQuest()
        {
            if (_snapshot == null || _snapshot.Quests.Count == 0) return null;
            if (!string.IsNullOrEmpty(_trackedQuest))
                foreach (var q in _snapshot.Quests) if (q.Id == _trackedQuest && !q.Claimed) return q;
            foreach (var q in _snapshot.Quests) if (!q.Claimed && q.Progress >= q.Target) return q;
            foreach (var q in _snapshot.Quests) if (!q.Claimed) return q;
            return null;
        }

        /// <summary>The id of the quest followed now: the list highlights it.</summary>
        public string TrackedQuestId { get { var q = ActiveQuest(); return q != null ? q.Id : null; } }

        /// <summary>The list of quests: a tap on a row follows that quest (a second tap goes back to the automatic choice).</summary>
        public void TrackQuest(string id)
        {
            _trackedQuest = _trackedQuest == id ? null : id;
            if (quest != null && quest.IsOpen) quest.OnSnapshot(_snapshot);
            RefreshQuestTracker();
        }

        /// <summary>The NPC the tracked quest sends the player to: Gôhan again once it is finished.</summary>
        string QuestTarget(QuestEntry q)
        {
            if (q == null) return null;
            return q.Progress >= q.Target ? "gohan" : q.NpcId;
        }

        // ---------------------------------------------------------------------------------------------------------------- snapshot: toast and tracker

        void OnQuestSnapshot(PlayerDataSnapshot snapshot)
        {
            if (snapshot == null) return;
            foreach (var q in snapshot.Quests)
            {
                bool done = q.Progress >= q.Target && !q.Claimed;
                bool was;
                if (_questSeen && done && !(_questDone.TryGetValue(q.Id, out was) && was))
                {
                    var npc = village != null ? village.NpcOfId("gohan") : null;
                    Toast("Xong nhiệm vụ: " + q.Title + " - về gặp " + (npc != null ? npc.Name : "Ông Gôhan"));
                }
                _questDone[q.Id] = done;
            }
            _questSeen = true;
            if (quest != null && quest.IsOpen) quest.OnSnapshot(snapshot);
            RefreshQuestTracker();
        }

        void RefreshQuestTracker()
        {
            if (questTrackerText == null) return;
            var q = ActiveQuest();
            bool inDungeon = village != null && village.Map != null && village.Map.Flying;
            bool show = q != null && !inDungeon && !_demo;
            questTrackerBg.gameObject.SetActive(show);
            if (!show) return;
            bool done = q.Progress >= q.Target;
            string where = done ? "Về gặp Ông Gôhan nhận thưởng" : !string.IsNullOrEmpty(q.NpcId) ? "Đến " + NpcNameOf(q.NpcId) : "Làm trong bảng nhân vật";
            questTrackerText.text = "<color=#FFE873>Nhiệm vụ:</color> " + q.Title + (done ? "  <color=#7CFF7C>(xong)</color>" : "  (" + q.Progress + "/" + q.Target + ")") + "\n<color=#BFE6FF>" + where + "</color>";
        }

        string NpcNameOf(string id)
        {
            var spec = village != null ? village.NpcOfId(id) : null;
            return spec != null ? spec.Name : id;
        }

        // ---------------------------------------------------------------------------------------------------------------- the bouncing arrow

        static Sprite GuideSprite()
        {
            if (_guideSprite != null) return _guideSprite;
            const int W = 48, H = 60;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Func<int, int, bool> inside = (x, y) =>
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return false;
                if (y >= 30) return x >= 15 && x < 33 && y < H - 4;                     // the shaft of a downward arrow (y counts up from the tip)
                float half = (y - 4) / 26f * 21f;                                       // the head: a triangle with its tip at the bottom
                return y >= 4 && Mathf.Abs(x - 23.5f) <= half;
            };
            var pixels = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = Color.clear;
                    if (inside(x, y)) c = Color.Lerp(new Color(1f, 0.78f, 0.12f), new Color(1f, 0.97f, 0.45f), y / (float)H);
                    else
                        for (int dy = -2; dy <= 2 && c.a == 0f; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                                if (dx * dx + dy * dy <= 5 && inside(x + dx, y + dy)) { c = new Color(0.42f, 0.2f, 0.02f, 0.95f); break; }
                    pixels[y * W + x] = c;
                }
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            _guideSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 1f);
            return _guideSprite;
        }

        void EnsureGuide()
        {
            if (_guide != null) return;
            var go = new GameObject("QuestArrow", typeof(RectTransform), typeof(Image));
            _guide = (RectTransform)go.transform;
            _guide.anchorMin = _guide.anchorMax = Vector2.zero;
            _guide.pivot = new Vector2(0.5f, 0f);
            _guide.sizeDelta = new Vector2(48f, 60f);
            _guideImage = go.GetComponent<Image>();
            _guideImage.sprite = GuideSprite();
            _guideImage.raycastTarget = false;
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(_guide, false);
            _guideLabel = labelGo.GetComponent<Text>();
            _guideLabel.font = toastText != null ? toastText.font : null;
            _guideLabel.fontSize = 18;
            _guideLabel.fontStyle = FontStyle.Bold;
            _guideLabel.color = new Color(1f, 0.95f, 0.5f);
            _guideLabel.alignment = TextAnchor.MiddleCenter;
            _guideLabel.raycastTarget = false;
            _guideLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelGo.AddComponent<Outline>().effectColor = new Color(0.15f, 0.08f, 0.02f, 0.95f);
            var lr = (RectTransform)labelGo.transform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.sizeDelta = new Vector2(220f, 26f);
            lr.anchoredPosition = new Vector2(0f, -2f);
            go.SetActive(false);
        }

        /// <summary>Over the NPC of the tracked quest, or at the screen edge towards it; hidden in a dungeon, in a dialog and while a panel covers the village.</summary>
        void UpdateQuestGuide()
        {
            var q = ActiveQuest();
            string npc = QuestTarget(q);
            bool ok = !string.IsNullOrEmpty(npc) && village != null && village.Map != null && !village.Map.Flying && !_demo && !popup.IsOpen;
            Vector2 feet = Vector2.zero;
            float h = 0f;
            if (ok) ok = village.TargetInfo(npc, out feet, out h);
            if (!ok)
            {
                if (_guide != null && _guide.gameObject.activeSelf) _guide.gameObject.SetActive(false);
                return;
            }
            EnsureGuide();
            float left, right, insetLeft, insetRight;
            village.VisibleRange(out left, out right, out insetLeft, out insetRight);
            float bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.5f)) * 16f;
            if (feet.x >= left - 10f && feet.x <= right + 10f)
            {
                if (_guide.parent != village.WorldLayer) _guide.SetParent(village.WorldLayer, false);
                _guide.SetAsLastSibling();
                _guide.localRotation = Quaternion.identity;
                _guide.anchoredPosition = feet + new Vector2(0f, h + 64f + bounce);
                _guideLabel.gameObject.SetActive(false);
            }
            else
            {
                bool toLeft = feet.x < left;
                var canvasRoot = transform;
                if (_guide.parent != canvasRoot)
                {
                    _guide.SetParent(canvasRoot, false);
                    _guide.SetSiblingIndex(Mathf.Min(canvasRoot.childCount - 1, village.viewport.GetSiblingIndex() + 1));
                }
                float side = toLeft ? insetLeft + 54f : village.ViewWidth - insetRight - 54f;
                _guide.localRotation = Quaternion.Euler(0f, 0f, toLeft ? -90f : 90f);
                _guide.pivot = new Vector2(0.5f, 0.5f);
                _guide.anchoredPosition = new Vector2(side + (toLeft ? -bounce : bounce), 330f);
                _guideLabel.gameObject.SetActive(true);
                _guideLabel.text = NpcNameOf(npc);
                _guideLabel.rectTransform.localRotation = Quaternion.Euler(0f, 0f, toLeft ? 90f : -90f);
            }
            if (feet.x >= left - 10f && feet.x <= right + 10f) _guide.pivot = new Vector2(0.5f, 0f);
            if (!_guide.gameObject.activeSelf) _guide.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------------------------------------------------------- Ông Gôhan: dialog and list

        void OpenQuestDialog(NpcSpec npc)
        {
            var q = ActiveQuest();
            int ready = BbQuestPanel.Claimable(_snapshot);
            var lines = new StringBuilder();
            if (_snapshot != null && _snapshot.Quests.Count > 0)
            {
                QuestEntry main = null;
                int daily = 0, dailyDone = 0;
                foreach (var e in _snapshot.Quests)
                {
                    if (e.Kind == "DAILY") { daily++; if (e.Claimed) dailyDone++; }
                    else main = e;
                }
                if (main != null)
                    lines.Append("<color=#1E8F1E>Nhiệm vụ chính:</color>\n<color=#1E5AB4>").Append(main.Title).Append(" (").Append(main.Progress).Append("/").Append(main.Target).Append(")</color>");
                else lines.Append("<color=#1E8F1E>Cháu đã làm xong mọi nhiệm vụ chính!</color>");
                if (daily > 0) lines.Append("\n<color=#B4641E>Hằng ngày: ").Append(dailyDone).Append("/").Append(daily).Append(" đã nhận thưởng</color>");
            }
            string desc = ready > 0 ? "Cháu làm xong việc rồi đấy! Nhận thưởng đi nào."
                                    : q != null ? "Cứ đi theo mũi tên vàng nhé: " + q.Title + "." : "Hôm nay cháu đã làm hết việc, mai ghé lại nhé.";
            var specs = new List<BbPopup.ButtonSpec>
            {
                new BbPopup.ButtonSpec(ready > 0 ? "Nhận thưởng\n(" + ready + ")" : "Nhận\nthưởng", ready > 0, true),
                new BbPopup.ButtonSpec("Bảng\nnhiệm vụ", true, false),
            };
            WirePopupSide();
            popup.centerOnScreen = true;
            popup.Show(null, npc.Name, lines.ToString(), desc, null, null, specs, i =>
            {
                popup.Hide();
                OpenQuest(i == 0);
            });
        }

        void OpenQuest(bool claimNow)
        {
            if (!_demo && !BbSession.IsActive) { Toast("Nhiệm vụ cần kết nối máy chủ"); return; }
            if (shop != null && shop.IsOpen) shop.Close();
            if (smith != null && smith.IsOpen) smith.Close();
            if (attendance != null && attendance.IsOpen) attendance.Close();
            WirePopupSide();
            popup.Hide();
            if (!_dockRight) _savedOpen = _panelOpen;
            SetDock(true);
            SetPanelOpen(true, false);
            quest.Open(_snapshot, false);
            if (claimNow) quest.Claim("");
        }

        /// <summary>For the screenshot tool: his dialog with this snapshot.</summary>
        public void DemoQuestDialog(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            OpenQuestDialog(new NpcSpec { Id = "gohan", Name = "Ông Gôhan" });
        }

        /// <summary>For the screenshot tool: the list of quests.</summary>
        public void DemoQuest(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            WirePopupSide();
            popup.Hide();
            if (!_dockRight) _savedOpen = _panelOpen;
            SetDock(true);
            SetPanelOpen(true, true);
            quest.Open(snapshot, true);
        }

        /// <summary>For the scripted run: open his dialog as if the fist had talked to him.</summary>
        public void DebugOpenQuest(bool claimNow) { OpenQuest(claimNow); }

        /// <summary>For the scripted run: where the arrow is now (the NPC it points to), or null.</summary>
        public string DebugGuideNpc { get { return _guide != null && _guide.gameObject.activeSelf ? QuestTarget(ActiveQuest()) : null; } }
    }
}
