using System;
using System.Collections.Generic;
using System.IO;
using NroRig;
using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// The poses a character shows when it uses a skill, as the Nro client plays them (owner request 2026-10-08: the punches need their kicks, the charge skills
    /// their own frames). Data: data/skill/skill_poses.json made by tools/build_assets.py from the Nro skill paints (GameScr.readSkill: per paint the stand and
    /// fly lists of poses, one pose per painted frame) and the paint id of every level (skill_template skills[level-1].id).
    /// Rules of Nro kept here (Char.setAutoSkillPaint, Char.update): a punch skill plays one of the first (level + 3) combos at random (Kick_1, Kick_2 and Kick_High
    /// are in them); Quả cầu kênh khi rises (Jump_Up) for 20 frames then holds Kame_Release_Fly while it charges; Makankosappo and Tự phát nổ hold Charge_Ki while
    /// they charge (Fall_Down in the air for the non-Xayda); Biến hình (the monkey) plays its paint then holds Charge_Ki while it waits.
    /// </summary>
    public static class BbSkillPoses
    {
        /// <summary>Painted frames per second of the Nro client (one pose of a skill paint per frame).</summary>
        public const float FramesPerSecond = 30f;

        [Serializable] class SkillRow { public int id; public int[] paints; }
        [Serializable] class PaintRow { public int id; public int[] stand, fly; }
        [Serializable] class FileData { public SkillRow[] skills; public PaintRow[] paints; }

        static Dictionary<int, int[]> _skills;
        static Dictionary<int, PaintRow> _paints;
        static readonly System.Random Rng = new System.Random();

        // the punch skills and the first paint of their combos (Char.setAutoSkillPaint): Dragon, Demon, Galick, Kaioken, Liên hoàn
        static readonly Dictionary<int, int> ComboBase = new Dictionary<int, int> { { 0, 0 }, { 2, 14 }, { 4, 28 }, { 9, 63 }, { 17, 63 } };         // Liên hoàn (Namếc): its paints 107..113 are remapped to 63..69 by Char.setSkillPaint, so it plays the Kaioken combos
        static readonly Dictionary<int, int> ComboCount = new Dictionary<int, int> { { 0, 7 }, { 2, 7 }, { 4, 7 }, { 9, 7 }, { 17, 7 } };

        static void Load()
        {
            if (_skills != null) return;
            _skills = new Dictionary<int, int[]>();
            _paints = new Dictionary<int, PaintRow>();
            try
            {
                string file = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "skill", "skill_poses.json");
                if (!File.Exists(file)) return;
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(file));
                if (data == null) return;
                if (data.skills != null) foreach (var s in data.skills) _skills[s.id] = s.paints;
                if (data.paints != null) foreach (var p in data.paints) _paints[p.id] = p;
            }
            catch (Exception) { }
        }

        public static bool Loaded { get { Load(); return _paints.Count > 0; } }

        /// <summary>What a skill plays: the poses, and the paint (whose frames carry the effects and the dart) with the list it used; paint -1 = none.</summary>
        public struct Picked
        {
            public int[] poses;
            public int paint;
            public bool fly;
        }

        /// <summary>The paint of this skill at this level (the first one of a combo), -1 when it has none.</summary>
        public static int PaintOf(uint skillId, uint level)
        {
            Load();
            int[] paints;
            if (!_skills.TryGetValue((int)skillId, out paints) || paints == null || paints.Length == 0) return -1;
            return paints[Mathf.Clamp((int)Math.Max(1u, level), 1, paints.Length) - 1];
        }

        /// <summary>The poses of this skill at this level (on the ground or flying); empty when the skill has no paint (the ultimates, the shield).</summary>
        public static int[] Use(uint skillId, uint level, bool flying)
        {
            return UsePicked(skillId, level, flying).poses;
        }

        public static Picked UsePicked(uint skillId, uint level, bool flying)
        {
            Load();
            var none = new Picked { poses = new int[0], paint = -1 };
            int[] paints;
            if (!_skills.TryGetValue((int)skillId, out paints) || paints == null || paints.Length == 0) return none;
            int lv = Mathf.Clamp((int)Math.Max(1u, level), 1, paints.Length);
            int paint = paints[lv - 1];
            int first;
            if (ComboBase.TryGetValue((int)skillId, out first))
            {
                // Nro: Res.random(0, level + 3) - 1, kept between the first and the last combo
                int pick = Mathf.Clamp(Rng.Next(0, lv + 3) - 1, 0, ComboCount[(int)skillId] - 1);
                paint = first + pick;
            }
            PaintRow row;
            if (!_paints.TryGetValue(paint, out row)) return none;
            bool fly = flying && row.fly != null && row.fly.Length > 0;
            int[] poses = fly ? row.fly : row.stand;
            if (poses == null || (poses.Length == 1 && poses[0] == 0)) return new Picked { poses = new int[0], paint = paint, fly = fly };        // one standing pose: no pose to show
            return new Picked { poses = poses, paint = paint, fly = fly };
        }

        /// <summary>
        /// The poses of a skill that charges first (the server announces it with CAST_START and its duration): the charge of Nro, then what the skill paints when it
        /// is released. The ultimates have no paint in the Nro data: they hold Kame_Charge and fire with Kame_Fire and Kame_Release.
        /// </summary>
        public static int[] Charge(uint skillId, uint level, bool flying, bool xayda, float seconds)
        {
            int fxFrom, paint;
            bool fly;
            return Charge(skillId, level, flying, xayda, seconds, out fxFrom, out paint, out fly);
        }

        /// <summary>As above; {@code fxFrom} is the pose where the paint of the skill (its effects and dart) starts, -1 when the poses carry none.</summary>
        public static int[] Charge(uint skillId, uint level, bool flying, bool xayda, float seconds, out int fxFrom, out int paint, out bool fly)
        {
            fxFrom = -1;
            paint = -1;
            fly = false;
            int hold = Mathf.Max(1, Mathf.RoundToInt(seconds * FramesPerSecond));
            var list = new List<int>();
            switch (skillId)
            {
                case 10:                                                       // Quả cầu kênh khi: up in the air, then the arms above the head while the ball grows
                    for (int i = 0; i < Math.Min(20, hold); i++) list.Add((int)NroPose.Jump_Up);
                    for (int i = 20; i < hold; i++) list.Add((int)NroPose.Kame_Release_Fly);
                    break;
                case 11:
                case 14:                                                       // Makankosappo, Tự phát nổ: the charge on the spot
                    int pose = flying && !xayda ? (int)NroPose.Fall_Down : (int)NroPose.Charge_Ki;
                    for (int i = 0; i < hold; i++) list.Add(pose);
                    break;
                case 24:
                case 25:                                                       // Super Kamejoko, Cađíc liên hoàn chưởng: the gong of the beam
                    for (int i = 0; i < hold; i++) list.Add(flying ? (int)NroPose.Kame_Charge_Fly : (int)NroPose.Kame_Charge);
                    for (int i = 0; i < 12; i++) list.Add(flying ? (int)NroPose.Kame_Fire_Fly : (int)NroPose.Kame_Fire);
                    for (int i = 0; i < 8; i++) list.Add(flying ? (int)NroPose.Kame_Release_Fly : (int)NroPose.Kame_Release);
                    return list.ToArray();
                default:                                                       // Ma phong ba and the rest: the charge, then the blast
                    for (int i = 0; i < hold; i++) list.Add((int)NroPose.Charge_Ki);
                    for (int i = 0; i < 12; i++) list.Add((int)NroPose.Charge_Blast);
                    return list.ToArray();
            }
            var released = UsePicked(skillId, level, flying);
            fxFrom = list.Count;
            paint = released.paint;
            fly = released.fly;
            list.AddRange(released.poses);
            return list.ToArray();
        }

        /// <summary>Biến hình (the monkey, Char.isWaitMonkey): its paint, then Charge_Ki held while the body changes.</summary>
        public static int[] Monkey(uint level)
        {
            var list = new List<int>(Use(13, level, false));
            for (int i = 0; i < Mathf.RoundToInt(1.2f * FramesPerSecond); i++) list.Add((int)NroPose.Charge_Ki);
            return list.ToArray();
        }

        /// <summary>Seconds to play these poses at the Nro pace.</summary>
        public static float Seconds(int[] poses)
        {
            return poses == null ? 0f : poses.Length / FramesPerSecond;
        }
    }
}
