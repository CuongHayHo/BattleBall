using System;
using System.Collections.Generic;
using BattleBall.Proto;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The Aura tab of the menu (owner decision 2026-10-08): every aura of the game on pages of 16, each one as a still picture in its box; the auras the account has not unlocked
    /// yet are faded and show the level that unlocks them (an aura is unlocked when the highest character reaches its level). Tap one for the preview and the button
    /// that wears it. The boxes show still pictures (loaded at once, nothing runs while the tab is open); only the preview of the popup animates.
    /// The hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbMenuAuras : MonoBehaviour
    {
        public GameObject root;
        public Text headerText;
        public Button[] slots;
        public Image[] slotImages;
        public Image[] thumbs;                  // a still picture of each aura: the tab does not animate (owner request 2026-10-08)
        public CanvasGroup[] thumbGroups;
        public Text[] slotLabels;
        public Button prev, next;
        public Text pageText;
        public Sprite slotEmpty, slotWorn;

        /// <summary>Called with the aura a player tapped.</summary>
        public Action<AuraInfo> Tapped;

        readonly List<AuraInfo> _list = new List<AuraInfo>();
        PlayerDataSnapshot _snapshot;
        int _page;
        bool _wired;

        public bool IsOpen { get { return root != null && root.activeSelf; } }
        public int PerPage { get { return slots.Length; } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            for (int i = 0; i < slots.Length; i++)
            {
                int slot = i;
                slots[i].onClick.AddListener(() => OnSlot(slot));
            }
            prev.onClick.AddListener(() => Turn(-1));
            next.onClick.AddListener(() => Turn(1));
        }

        void Awake() { Wire(); }

        public void Show(PlayerDataSnapshot snapshot)
        {
            Wire();
            root.SetActive(true);
            Refresh(snapshot);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        public static uint HighestLevel(PlayerDataSnapshot s)
        {
            uint top = 0;
            if (s != null) foreach (var c in s.Characters) top = Math.Max(top, c.Level);
            return top;
        }

        /// <summary>The auras in the order a player reaches them: by unlock level, then by id.</summary>
        void Rebuild()
        {
            _list.Clear();
            if (_snapshot != null) _list.AddRange(_snapshot.Auras);
            _list.Sort((a, b) => a.UnlockLevel != b.UnlockLevel ? a.UnlockLevel.CompareTo(b.UnlockLevel) : a.AuraId.CompareTo(b.AuraId));
        }

        public void Refresh(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            Rebuild();
            int pages = Mathf.Max(1, (_list.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            prev.gameObject.SetActive(pages > 1);
            next.gameObject.SetActive(pages > 1);
            pageText.text = pages > 1 ? (_page + 1) + "/" + pages : "";
            uint level = HighestLevel(snapshot);
            uint worn = snapshot != null ? snapshot.EquippedAuraId : 0;
            int unlocked = 0;
            AuraInfo wornInfo = null;
            foreach (var a in _list)
            {
                if (a.UnlockLevel <= level) unlocked++;
                if (a.AuraId == worn) wornInfo = a;
            }
            headerText.text = "Aura\nĐang dùng: " + (wornInfo != null ? NameOf(wornInfo) : "chưa trang bị") + "\nĐã mở " + unlocked + " / " + _list.Count + "  (cấp cao nhất " + level + ")";
            for (int i = 0; i < PerPage; i++)
            {
                int index = _page * PerPage + i;
                bool has = index < _list.Count;
                slots[i].gameObject.SetActive(has);
                if (!has) continue;
                var a = _list[index];
                bool open = a.UnlockLevel <= level;
                bool isWorn = a.AuraId == worn;
                slotImages[i].sprite = isWorn ? slotWorn : slotEmpty;
                thumbGroups[i].alpha = open ? 1f : 0.28f;
                var picture = BbAuraAssets.GetThumb(a);
                thumbs[i].sprite = picture;
                thumbs[i].enabled = picture != null;
                slotLabels[i].text = isWorn ? "Đang dùng" : open ? NameOf(a) : "Cấp " + a.UnlockLevel;
                slotLabels[i].color = open ? Color.white : new Color(1f, 0.75f, 0.35f);
            }
        }

        public static string NameOf(AuraInfo a)
        {
            return a.Name.Length > 0 ? a.Name : "Aura " + a.AuraId;
        }

        void Turn(int delta)
        {
            _page += delta;
            int pages = Mathf.Max(1, (_list.Count + PerPage - 1) / PerPage);
            _page = (_page + pages) % pages;
            Refresh(_snapshot);
        }

        void OnSlot(int slot)
        {
            int index = _page * PerPage + slot;
            if (index < _list.Count && Tapped != null) Tapped(_list[index]);
        }

        /// <summary>For the screenshot tool: the page that holds this aura.</summary>
        public void DemoPageOf(uint auraId)
        {
            Rebuild();
            int index = _list.FindIndex(a => a.AuraId == auraId);
            if (index >= 0) _page = index / PerPage;
            Refresh(_snapshot);
        }

        /// <summary>For the screenshot tool: tap the aura with this id.</summary>
        public void DemoTap(uint auraId)
        {
            var a = _list.Find(x => x.AuraId == auraId);
            if (a != null && Tapped != null) Tapped(a);
        }

    }
}
