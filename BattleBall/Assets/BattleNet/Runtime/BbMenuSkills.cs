using System;
using System.Collections.Generic;
using System.Globalization;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;
using CharacterInfo = BattleBall.Proto.CharacterInfo;

namespace BattleBall.Net
{
    /// <summary>
    /// The Kỹ năng tab (owner request 2026-10-08): the skills of the three characters in one list, grouped by planet (Trái Đất, Xayda, Namếc), each row as the skill
    /// list of the Nro panel (icon, name, level and what the next level costs). A skill is learned (level 1) and raised one level at a time with the gold of the
    /// account (UpgradeSkillRequest, EconomyService.upgradeSkill); the punches and skills stop at level 7, the ultimates at 9 (GDD 4, skill_template.max_level).
    /// Every step also costs skill cards (owner request 2026-10-11, docs 15.32): as many as the place of the skill in the list of its planet (1 for the punch ... 9 for the ultimate); the cards come from quests.
    /// A tap on a row opens the details of the current and the next level in the popup. The list is made at run time under <see cref="content"/>.
    /// </summary>
    public sealed class BbMenuSkills : MonoBehaviour
    {
        public GameObject root;
        public Text headerText;
        public ScrollRect scroll;
        public RectTransform content;
        public Font font;
        public Sprite rowSprite, iconFrame, buttonSprite;
        public BbPopup popup;

        /// <summary>Called with the character and the skill to learn or raise.</summary>
        public Action<ulong, SkillCatalogEntry> Upgrade;

        static readonly string[] Races = { "EARTH", "XAYDA", "NAMEC" };
        static readonly string[] Titles = { "Trái Đất", "Xayda", "Namếc" };
        static readonly Color NameBlue = new Color(0.12f, 0.24f, 0.62f), Ink = new Color(0.30f, 0.14f, 0.05f), Grey = new Color(0.45f, 0.40f, 0.35f);

        PlayerDataSnapshot _snapshot;
        readonly List<GameObject> _rows = new List<GameObject>();

        public bool IsOpen { get { return root != null && root.activeSelf; } }

        public void Show(PlayerDataSnapshot snapshot)
        {
            root.SetActive(true);
            Refresh(snapshot);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        public void Refresh(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            foreach (var r in _rows) Kill(r);
            _rows.Clear();
            ulong gold = snapshot != null ? snapshot.Gold : 0, cards = BbQuestPanel.CardsOf(snapshot);
            int learned = 0, total = 0;
            if (snapshot != null)
            {
                for (int i = 0; i < Races.Length; i++)
                {
                    var ch = CharacterOf(Races[i]);
                    if (ch == null) continue;
                    AddHeader(Titles[i] + "  (cấp " + ch.Level + ")");
                    foreach (var entry in SkillsOf(Races[i]))
                    {
                        uint lv = LevelOf(ch, entry.SkillId);
                        total++;
                        if (lv > 0) learned++;
                        AddRow(ch, entry, lv, gold, cards);
                    }
                }
            }
            headerText.text = "Kỹ năng\nĐã học " + learned + " / " + total + "\nVàng: " + Money(gold) + "   Thẻ: " + Money(cards);
        }

        CharacterInfo CharacterOf(string race)
        {
            if (_snapshot == null) return null;
            foreach (var c in _snapshot.Characters) if (c.Race == race) return c;
            return null;
        }

        /// <summary>The skills of a planet by their slot on the skill bar, Khiên năng lượng (common to all) among them.</summary>
        List<SkillCatalogEntry> SkillsOf(string race)
        {
            var list = new List<SkillCatalogEntry>();
            foreach (var e in _snapshot.SkillCatalog) if (e.Race == race || e.Race == "COMMON") list.Add(e);
            list.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.SkillId.CompareTo(b.SkillId));
            return list;
        }

        static uint LevelOf(CharacterInfo ch, uint skillId)
        {
            foreach (var s in ch.Skills) if (s.SkillId == skillId) return s.Level;
            return 0;
        }

        static SkillLevelInfo Level(SkillCatalogEntry e, uint level)
        {
            foreach (var l in e.Levels) if (l.Level == level) return l;
            return null;
        }

        // ---------------------------------------------------------------------------------------------------------------- rows

        void AddHeader(string text)
        {
            var go = NewRect("Header", content);
            go.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var bg = go.gameObject.AddComponent<Image>();
            bg.sprite = rowSprite;
            bg.color = new Color(0.60f, 0.48f, 0.33f);
            bg.raycastTarget = false;
            var t = NewText(go, text, 17, new Color(1f, 0.93f, 0.55f), TextAnchor.MiddleLeft, true);
            Stretch(t.rectTransform, 10f, 0f);
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0.15f, 0.08f, 0.02f, 0.9f);
            _rows.Add(go.gameObject);
        }

        void AddRow(CharacterInfo ch, SkillCatalogEntry e, uint level, ulong gold, ulong cards)
        {
            var go = NewRect("Skill" + e.SkillId, content);
            go.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            var bg = go.gameObject.AddComponent<Image>();
            bg.sprite = rowSprite;
            bg.color = new Color(0.906f, 0.875f, 0.824f);                // #E7DFD2, the rows of the Nro lists
            var rowButton = go.gameObject.AddComponent<Button>();
            rowButton.targetGraphic = bg;
            var colors = rowButton.colors;
            colors.pressedColor = new Color(0.98f, 1f, 0.29f);
            rowButton.colors = colors;
            var character = ch;
            var entry = e;
            rowButton.onClick.AddListener(() => ShowDetails(character, entry));

            // the icon in the orange frame of the Nro skill buttons
            var frame = NewRect("Frame", go);
            Place(frame, 4f, 4f, 48f, 48f);
            var fi = frame.gameObject.AddComponent<Image>();
            fi.sprite = iconFrame;
            fi.raycastTarget = false;
            var resolver = NroSpriteResolver.Instance;
            Sprite icon = e.IconId > 0 && resolver != null ? resolver.ResolveSprite((int)e.IconId, BbAssetCache.Scale) : null;
            var ir = NewRect("Icon", frame);
            Stretch(ir, 7f, 7f);
            var ii = ir.gameObject.AddComponent<Image>();
            ii.sprite = icon;
            ii.enabled = icon != null;
            ii.preserveAspect = true;
            ii.raycastTarget = false;
            if (level == 0) ii.color = new Color(0.55f, 0.55f, 0.55f);

            var name = NewText(go, e.Name, 16, level > 0 ? NameBlue : Grey, TextAnchor.MiddleLeft, true);
            Place(name.rectTransform, 58f, 4f, 180f, 24f);
            var next = Level(e, level + 1);
            string info = level == 0 ? "Chưa học" : "Cấp " + level + "/" + e.MaxLevel;
            if (next != null) info += "  " + Money(next.GoldCost) + " vàng" + (next.CardCost > 0 ? " + " + next.CardCost + " thẻ" : "");
            else info += "   Đã đạt cấp tối đa";
            var line = NewText(go, info, 13, Ink, TextAnchor.MiddleLeft, false);
            Place(line.rectTransform, 58f, 28f, 190f, 22f);

            // the button: Học (learn) or Nâng (raise), grey when the gold is short, none at the maximum level
            if (next != null)
            {
                var br = NewRect("Upgrade", go);
                Place(br, 312f - 66f, 9f, 60f, 38f);
                var bi = br.gameObject.AddComponent<Image>();
                bi.sprite = buttonSprite;
                bi.color = gold >= next.GoldCost && cards >= next.CardCost ? Color.white : new Color(0.62f, 0.62f, 0.62f);     // grey: the gold or the cards are short (the server says so if pressed)
                bi.type = Image.Type.Sliced;
                var b = br.gameObject.AddComponent<Button>();
                b.targetGraphic = bi;
                b.onClick.AddListener(() => { if (Upgrade != null) Upgrade(character.CharacterId, entry); });
                var bt = NewText(br, level == 0 ? "Học" : "Nâng", 15, Ink, TextAnchor.MiddleCenter, true);
                Stretch(bt.rectTransform, 2f, 2f);
            }
            _rows.Add(go.gameObject);
        }

        /// <summary>The popup of a skill: what it does, its numbers at the current and the next level, and the button that learns or raises it.</summary>
        void ShowDetails(CharacterInfo ch, SkillCatalogEntry e)
        {
            if (popup == null) return;
            uint level = LevelOf(ch, e.SkillId);
            var cur = Level(e, level);
            var next = Level(e, level + 1);
            var lines = new System.Text.StringBuilder();
            lines.Append("<color=#1E3C96>").Append(level == 0 ? "Chưa học" : "Cấp " + level + " / " + e.MaxLevel).Append("</color>");
            if (cur != null) lines.Append('\n').Append(Numbers(cur, "#1E8F1E"));
            if (next != null) lines.Append("\n<color=#7A5A3A>Cấp " + next.Level + ":</color>\n").Append(Numbers(next, "#B4641E"));
            string desc = e.Description;
            var shown = cur ?? next;
            if (shown != null && shown.DamagePctAtk > 0) desc = desc.Replace("#", Num(shown.DamagePctAtk));
            desc = desc.Replace("#", "...");
            var specs = new List<BbPopup.ButtonSpec>();
            ulong gold = _snapshot != null ? _snapshot.Gold : 0, cards = BbQuestPanel.CardsOf(_snapshot);
            if (next != null)
                specs.Add(new BbPopup.ButtonSpec((level == 0 ? "Học\n" : "Nâng cấp\n") + Money(next.GoldCost) + " vàng" + (next.CardCost > 0 ? " + " + next.CardCost + " thẻ" : ""),
                                                 gold >= next.GoldCost && cards >= next.CardCost, true));
            var resolver = NroSpriteResolver.Instance;
            Sprite icon = e.IconId > 0 && resolver != null ? resolver.ResolveSprite((int)e.IconId, BbAssetCache.Scale) : null;
            popup.Show(icon, e.Name, lines.ToString(), Titles[Array.IndexOf(Races, ch.Race) < 0 ? 0 : Array.IndexOf(Races, ch.Race)] + ". " + desc, null, null, specs,
                i => { popup.Hide(); if (Upgrade != null) Upgrade(ch.CharacterId, e); });
        }

        static string Numbers(SkillLevelInfo l, string color)
        {
            var parts = new List<string>();
            if (l.DamagePctAtk > 0) parts.Add("Sát thương " + Num(l.DamagePctAtk) + "% Sức đánh");
            parts.Add("Hồi chiêu " + Num(l.CooldownMs / 1000.0) + " giây");
            if (l.KiCostPct > 0) parts.Add("Tốn " + Num(l.KiCostPct) + "% Ki");
            return "<color=" + color + ">" + string.Join("\n", parts.ToArray()) + "</color>";
        }

        static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        static string Money(ulong v)
        {
            return v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
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

        /// <summary>For the screenshot tool: open the details of a skill of the character of this race.</summary>
        public void DemoDetails(string race, uint skillId)
        {
            var ch = CharacterOf(race);
            if (ch == null) return;
            foreach (var e in _snapshot.SkillCatalog) if (e.SkillId == skillId) { ShowDetails(ch, e); return; }
        }
    }
}
