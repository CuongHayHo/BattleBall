using System;
using System.Collections.Generic;
using BattleBall.Proto;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The list of dungeons of the NPC Cui (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25): a box in the middle of the screen with the dungeons of the
    /// server (PlayerDataSnapshot.dungeons) on pages of six, the details of the one chosen, the team that goes (one character and two AI, or the fused form and one AI) and the
    /// buttons Vào and Đóng. The hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbDungeonPicker : MonoBehaviour
    {
        public GameObject root;
        public Text title, teamText, detailText, pageText;
        public Button[] rows;
        public Image[] rowImages;
        public Text[] rowNames, rowInfos;
        public Button prev, next, enter, close;
        public Sprite rowIdle, rowSelected;

        /// <summary>The dungeon the player chose and wants to enter.</summary>
        public Action<DungeonInfo> Entered;
        public Action Closed;

        static readonly Color SelectedInk = new Color(0.30f, 0.14f, 0.05f), InfoInk = new Color(1f, 0.93f, 0.25f);
        readonly List<DungeonInfo> _list = new List<DungeonInfo>();
        int _page, _selected = -1;
        bool _wired;

        public bool IsOpen { get { return root != null && root.activeSelf; } }
        public int PerPage { get { return rows.Length; } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            for (int i = 0; i < rows.Length; i++)
            {
                int row = i;
                rows[i].onClick.AddListener(() => Select(_page * PerPage + row));
            }
            prev.onClick.AddListener(() => Turn(-1));
            next.onClick.AddListener(() => Turn(1));
            enter.onClick.AddListener(() =>
            {
                if (_selected >= 0 && _selected < _list.Count && Entered != null) Entered(_list[_selected]);
            });
            close.onClick.AddListener(Hide);
        }

        void Awake() { Wire(); }

        /// <summary>Opens the list. <paramref name="team"/> is the line that says who goes ("Trái Đất cấp 12 + 2 AI").</summary>
        public void Show(string npcName, PlayerDataSnapshot snapshot, string team, bool canEnter)
        {
            Wire();
            title.text = npcName + ": chọn phó bản";
            teamText.text = team ?? "";
            _list.Clear();
            if (snapshot != null) _list.AddRange(snapshot.Dungeons);
            _list.Sort((a, b) => a.DungeonId.CompareTo(b.DungeonId));
            if (_selected >= _list.Count) _selected = -1;
            _page = _selected >= 0 ? _selected / PerPage : 0;
            _canEnter = canEnter;
            root.SetActive(true);
            Refresh();
        }

        bool _canEnter = true;

        public void Hide()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            var closed = Closed;
            if (closed != null) closed();
        }

        /// <summary>Closes the box without the Closed callback (the ship takes over).</summary>
        public void HideQuietly()
        {
            if (root != null) root.SetActive(false);
        }

        void Select(int index)
        {
            if (index < 0 || index >= _list.Count) return;
            _selected = index;
            Refresh();
        }

        void Turn(int delta)
        {
            int pages = Mathf.Max(1, (_list.Count + PerPage - 1) / PerPage);
            _page = (_page + delta + pages) % pages;
            Refresh();
        }

        public static string TierName(string tier)
        {
            switch (tier)
            {
                case "early": return "vùng sớm";
                case "mid": return "vùng giữa";
                case "late": return "vùng cuối";
                default: return tier;
            }
        }

        static string Minutes(uint seconds)
        {
            return seconds % 60 == 0 ? (seconds / 60) + " phút" : (seconds / 60) + " phút " + (seconds % 60) + " giây";
        }

        void Refresh()
        {
            int pages = Mathf.Max(1, (_list.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            prev.gameObject.SetActive(pages > 1);
            next.gameObject.SetActive(pages > 1);
            pageText.text = pages > 1 ? (_page + 1) + "/" + pages : "";
            for (int i = 0; i < rows.Length; i++)
            {
                int index = _page * PerPage + i;
                bool has = index < _list.Count;
                rows[i].gameObject.SetActive(has);
                if (!has) continue;
                var d = _list[index];
                bool chosen = index == _selected;
                rowImages[i].sprite = chosen ? rowSelected : rowIdle;
                rowNames[i].text = d.DungeonId + ". " + d.Name;
                rowInfos[i].text = "Cấp " + d.RecommendedLevel + "   " + d.BossCount + " boss";
                rowNames[i].color = chosen ? SelectedInk : Color.white;                 // the chosen row is yellow: dark letters read better on it
                rowInfos[i].color = chosen ? SelectedInk : InfoInk;
            }
            if (_list.Count == 0)
            {
                detailText.text = "Chưa có phó bản nào.";
            }
            else if (_selected < 0)
            {
                detailText.text = "Chạm vào một phó bản để xem.";
            }
            else
            {
                var d = _list[_selected];
                detailText.text = d.Name + " (" + TierName(d.Tier) + ")\nCấp đề nghị " + d.RecommendedLevel + ". Gặp " + d.BossCount + " boss, mỗi boss có các đợt quái nhỏ đi trước."
                                  + (d.TimeLimitS > 0 ? "\nGiới hạn thời gian: " + Minutes(d.TimeLimitS) + "." : "");
            }
            enter.interactable = _selected >= 0 && _canEnter;
        }

        /// <summary>For the screenshot tool and the play scenario: choose the dungeon with this id.</summary>
        public void DemoSelect(uint dungeonId)
        {
            int index = _list.FindIndex(d => d.DungeonId == dungeonId);
            if (index >= 0) { _page = index / PerPage; Select(index); }
        }

        /// <summary>For the play scenario: press Vào.</summary>
        public void DemoEnter()
        {
            if (enter.interactable && Entered != null) Entered(_list[_selected]);
        }
    }
}
