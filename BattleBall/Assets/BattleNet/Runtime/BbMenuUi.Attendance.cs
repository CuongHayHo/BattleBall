using System;
using System.Collections.Generic;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// The elder of the village and the daily check-in (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25). Talking to him asks the server for the table of the month and
    /// opens his dialog in the middle of the screen: today's gold, gems and free items and two buttons, "Điểm danh" (checks in) and "Bảng điểm danh" (the table of the month on the left,
    /// the player's panel moving to the right as beside a shop). The server decides everything.
    /// </summary>
    public sealed partial class BbMenuUi
    {
        bool _attendanceBusy;

        /// <summary>The picture of a free item of the table (the server sends its Nro icon id).</summary>
        public static Sprite RewardIcon(RewardItem it)
        {
            var resolver = NroSpriteResolver.Instance;
            return it != null && it.IconId > 0 && resolver != null ? resolver.ResolveSprite((int)it.IconId, BbAssetCache.Scale) : null;
        }

        async void OpenAttendanceDialog(NpcSpec npc)
        {
            if (_attendanceBusy) return;
            if (_demo || !BbSession.IsActive) { Toast("Điểm danh cần kết nối máy chủ"); return; }
            _attendanceBusy = true;
            AttendanceInfo info = null;
            try { info = await BbSession.Client.AttendanceAsync(); }
            catch (Exception e) { Toast("Không mở được bảng điểm danh: " + e.Message); }
            _attendanceBusy = false;
            if (info != null) ShowAttendanceDialog(npc, info);
        }

        void ShowAttendanceDialog(NpcSpec npc, AttendanceInfo info)
        {
            int today = (int)info.Today;
            var day = today >= 1 && today <= info.Days.Count ? info.Days[today - 1] : null;
            var lines = new StringBuilder();
            if (day != null)
            {
                lines.Append("<color=#1E8F1E>").Append(info.ClaimedToday ? "Hôm nay đã nhận:" : "Điểm danh hôm nay nhận:").Append("</color>");
                foreach (string line in BbAttendancePanel.RewardLines(day).Split('\n')) lines.Append("\n<color=#1E5AB4>").Append(line).Append("</color>");
            }
            string desc = "Chào cháu! Hôm nay là ngày " + info.Today + "/" + info.Month + ". Tháng này cháu đã điểm danh " + info.ClaimedCount + "/" + info.DaysInMonth + " ngày."
                          + (info.ClaimedToday ? " Mai nhớ ghé lại nhé." : "");
            var specs = new List<BbPopup.ButtonSpec>
            {
                new BbPopup.ButtonSpec(info.ClaimedToday ? "Đã điểm\ndanh" : "Điểm danh", !info.ClaimedToday, true),
                new BbPopup.ButtonSpec("Bảng\nđiểm danh", true, false),
            };
            WirePopupSide();
            popup.centerOnScreen = true;
            popup.Show(null, npc.Name, lines.ToString(), desc, null, null, specs, i =>
            {
                popup.Hide();
                OpenAttendance(info, i == 0);
            });
        }

        void OpenAttendance(AttendanceInfo info, bool claimNow)
        {
            if (shop != null && shop.IsOpen) shop.Close();
            if (smith != null && smith.IsOpen) smith.Close();
            WirePopupSide();
            popup.Hide();
            if (!_dockRight) _savedOpen = _panelOpen;
            SetDock(true);
            SetPanelOpen(true, false);
            attendance.Open(info, _snapshot, false);
            if (claimNow) attendance.DebugClaim();
        }

        /// <summary>For the screenshot tool: the dialog of the elder with this table.</summary>
        public void DemoAttendanceDialog(AttendanceInfo info) { ShowAttendanceDialog(new NpcSpec { Id = "elder", Name = "Ông Trưởng làng" }, info); }

        /// <summary>For the screenshot tool: the table of the month.</summary>
        public void DemoAttendance(AttendanceInfo info)
        {
            WirePopupSide();
            popup.Hide();
            if (!_dockRight) _savedOpen = _panelOpen;
            SetDock(true);
            SetPanelOpen(true, true);
            attendance.Open(info, _snapshot, true);
        }
    }
}
