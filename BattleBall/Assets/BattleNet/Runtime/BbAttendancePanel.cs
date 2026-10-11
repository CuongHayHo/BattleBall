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
    /// The table of the daily check-in of the village elder (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25): the days of the month as a grid, every day with the
    /// picture of its free item (or the gems), a tick on the days already claimed, today marked; a tap on a day tells what it gives; the button checks in for today. Like the panel of
    /// Bà Hạt Mít it stands on the left and the player's panel goes to the right. The server decides everything (AttendanceRequest, ClaimAttendanceRequest).
    /// The hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbAttendancePanel : MonoBehaviour
    {
        public GameObject root;
        public RectTransform panelRect;
        public Image portrait;
        public Text headerText;
        public Button closeButton;
        public Text goldText, gemText;
        public Text titleText;
        public Button[] cellButtons;
        public Image[] cellBg, cellIcon;
        public Text[] cellDay, cellQty, cellCheck;
        public Text infoText, resultText;
        public Button actionButton;
        public Text actionLabel;
        public Sprite goldSprite, gemSprite;
        public BbMenuUi menu;

        /// <summary>Raised when the player closes the panel (the X).</summary>
        public Action Closed;

        const float PanelWidth = 320f;
        static readonly Color Claimed = new Color(0.62f, 0.86f, 0.55f), Missed = new Color(0.74f, 0.70f, 0.66f), Future = new Color(0.91f, 0.875f, 0.82f),
                              Today = new Color(0.98f, 1f, 0.29f), Picked = new Color(1f, 0.82f, 0.4f);
        static readonly Color Good = new Color(0.12f, 0.56f, 0.12f), Bad = new Color(0.72f, 0.12f, 0.12f), Ink = new Color(0.18f, 0.10f, 0.04f);

        AttendanceInfo _info;
        PlayerDataSnapshot _snapshot;
        int _selected;                                                     // the day whose reward the text tells (1-based)
        bool _open, _wired, _busy;
        float _x = -PanelWidth;

        public bool IsOpen { get { return _open; } }
        public float VisibleWidth { get { return Mathf.Clamp(PanelWidth + _x, 0f, PanelWidth); } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            closeButton.onClick.AddListener(Close);
            actionButton.onClick.AddListener(Claim);
            for (int i = 0; i < cellButtons.Length; i++)
            {
                int day = i + 1;
                cellButtons[i].onClick.AddListener(() => { _selected = day; Refresh(); });
            }
        }

        void Awake() { Wire(); }

        public void Open(AttendanceInfo info, PlayerDataSnapshot snapshot, bool immediate = false)
        {
            Wire();
            _info = info;
            _snapshot = snapshot;
            _selected = (int)info.Today;
            resultText.text = "";
            _open = true;
            root.SetActive(true);
            if (immediate) { _x = 0f; Apply(); }
            var sp = BbCostumeAvatar.Of(430);                              // the elder
            portrait.sprite = sp;
            portrait.enabled = sp != null;
            titleText.text = "Điểm danh";
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
            _snapshot = snapshot;
            if (_open) Refresh();
        }

        static string Money(ulong v) { return v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.'); }

        /// <summary>What a day gives, one line each: gold, gems, the free items.</summary>
        public static string RewardLines(AttendanceDay day)
        {
            var sb = new StringBuilder();
            if (day.Gold > 0) sb.Append("+").Append(Money(day.Gold)).Append(" vàng");
            if (day.Gems > 0) sb.Append(sb.Length > 0 ? "\n" : "").Append("+").Append(day.Gems).Append(" ngọc");
            foreach (var it in day.Items) sb.Append(sb.Length > 0 ? "\n" : "").Append(it.Name).Append(" x").Append(it.Quantity);
            return sb.ToString();
        }

        void Refresh()
        {
            if (!_open || _info == null) return;
            int days = (int)_info.DaysInMonth;
            int today = (int)_info.Today;
            headerText.text = "Ông Trưởng làng\nĐiểm danh tháng " + _info.Month + "/" + _info.Year + "\nĐã điểm danh " + _info.ClaimedCount + "/" + days + " ngày";
            for (int i = 0; i < cellButtons.Length; i++)
            {
                bool used = i < days && i < _info.Days.Count;
                cellButtons[i].gameObject.SetActive(used);
                if (!used) continue;
                var d = _info.Days[i];
                int day = i + 1;
                cellDay[i].text = day.ToString();
                Sprite sp = d.Items.Count > 0 ? BbMenuUi.RewardIcon(d.Items[0]) : gemSprite;
                cellIcon[i].sprite = sp;
                cellIcon[i].enabled = sp != null;
                cellQty[i].text = d.Items.Count > 0 ? "x" + d.Items[0].Quantity : "+" + d.Gems;
                cellCheck[i].gameObject.SetActive(d.Claimed);
                Color c = d.Claimed ? Claimed : day == today ? Today : day < today ? Missed : Future;
                if (day == _selected) c = Color.Lerp(c, Picked, 0.6f);
                cellBg[i].color = c;
                cellIcon[i].color = day < today && !d.Claimed ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            }
            var sel = _selected >= 1 && _selected <= _info.Days.Count ? _info.Days[_selected - 1] : null;
            if (sel != null)
            {
                string state = sel.Claimed ? "đã nhận" : _selected == today ? "hôm nay" : _selected < today ? "đã bỏ lỡ" : "chưa tới ngày";
                infoText.text = "Ngày " + _selected + "/" + _info.Month + " (" + state + ")\n" + RewardLines(sel);
            }
            bool can = !_info.ClaimedToday && !_busy;
            actionButton.interactable = can;
            actionLabel.text = _info.ClaimedToday ? "Đã điểm\ndanh" : "Điểm danh";
            if (_snapshot != null) { goldText.text = Money(_snapshot.Gold); gemText.text = Money(_snapshot.Gems); }
        }

        async void Claim()
        {
            if (_busy || _info == null || _info.ClaimedToday || !BbSession.IsActive) return;
            _busy = true;
            Refresh();
            try
            {
                var result = await BbSession.Client.ClaimAttendanceAsync();
                if (result.Ok)
                {
                    var before = _info.Days[(int)_info.Today - 1];
                    _info = await BbSession.Client.AttendanceAsync();
                    _selected = (int)_info.Today;
                    resultText.color = Good;
                    resultText.text = "Điểm danh thành công!\n" + RewardLines(before).Replace("\n", ", ");
                }
                else
                {
                    resultText.color = Bad;
                    resultText.text = result.Error == ActionError.AlreadyOwned ? "Hôm nay cháu đã điểm danh rồi" : "Không điểm danh được: " + result.Message;
                    _info = await BbSession.Client.AttendanceAsync();
                }
            }
            catch (Exception e)
            {
                resultText.color = Bad;
                resultText.text = "Không gửi được lệnh: " + e.Message;
            }
            _busy = false;
            Refresh();
        }

        // ---------------------------------------------------------------------------------------------------------------- for the scripted run and the screenshot tool

        public string DebugResult { get { return resultText.text; } }
        public bool DebugClaimed { get { return _info != null && _info.ClaimedToday; } }
        public void DebugClaim() { Claim(); }
        public void DemoSelect(int day) { _selected = day; Refresh(); }
    }
}
