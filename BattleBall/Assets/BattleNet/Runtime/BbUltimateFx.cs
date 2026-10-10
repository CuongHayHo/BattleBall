using System;
using System.Collections.Generic;
using System.IO;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The pictures of the three ultimates, after Effect_End of the Nro client (owner request 2026-10-09, stage 2b item 1): Super Kamejoko (24: a beam that grows
    /// from the hands), Cađíc liên hoàn chưởng (25: fifteen energy balls, one after the other, each ending in an explosion) and Ma phong ba (26: the jar at the
    /// feet, the vortex above, the wisp that falls into the jar, the jar shaking). Every one starts with the aura (Pow) and the sparks (Gong) of the charge.
    /// The sheets are res/x4/e/e_9 to e_28 of Nro (the default paint, typePaint 0), copied to data/effect_end; their frame sizes are the table arrInfoEff of
    /// Effect_End. The Nro client counts game ticks (about 30 a second): everything here is a function of the tick since the skill started.
    /// </summary>
    public sealed class BbUltimateFx : MonoBehaviour
    {
        public BbVillageView village;

        const float TickSeconds = 1f / 30f;
        const float DefaultBeam = 3360f;          // 2 chunks of the village map (1680 Nro px each), the reach of the ultimates (owner request 2026-10-10)
        const float Chest = 18f;                  // Nro px above the feet where the beam and the sparks are centred (Effect_End counts from the character's centre)

        struct SheetInfo
        {
            public int w, h, n;                   // frame width and the height of all frames in Nro px, number of frames (arrInfoEff)
            public SheetInfo(int w, int h, int n) { this.w = w; this.h = h; this.n = n; }
        }

        static readonly Dictionary<int, SheetInfo> Info = new Dictionary<int, SheetInfo>
        {
            { 9, new SheetInfo(92, 525, 7) }, { 10, new SheetInfo(62, 372, 6) }, { 11, new SheetInfo(80, 352, 4) }, { 12, new SheetInfo(80, 352, 4) },
            { 13, new SheetInfo(80, 352, 4) }, { 14, new SheetInfo(72, 240, 3) }, { 15, new SheetInfo(20, 42, 3) }, { 16, new SheetInfo(65, 160, 4) },
            { 17, new SheetInfo(50, 300, 6) }, { 18, new SheetInfo(84, 168, 2) }, { 19, new SheetInfo(90, 540, 6) }, { 20, new SheetInfo(180, 900, 6) },
            { 21, new SheetInfo(62, 186, 3) }, { 22, new SheetInfo(34, 80, 4) }, { 23, new SheetInfo(140, 560, 4) }, { 24, new SheetInfo(64, 600, 6) },
            { 25, new SheetInfo(36, 200, 5) }, { 26, new SheetInfo(35, 200, 5) }, { 27, new SheetInfo(50, 250, 5) }, { 28, new SheetInfo(50, 240, 6) },
        };

        static readonly Dictionary<int, Texture2D> Textures = new Dictionary<int, Texture2D>();
        static readonly Dictionary<long, Sprite[]> Sheets = new Dictionary<long, Sprite[]>();

        static Sprite[] Sheet(int id, bool bottom)
        {
            long key = id * 2L + (bottom ? 1 : 0);
            Sprite[] frames;
            if (Sheets.TryGetValue(key, out frames)) return frames;
            SheetInfo info;
            if (!Info.TryGetValue(id, out info)) return null;
            Texture2D tex;
            if (!Textures.TryGetValue(id, out tex) || tex == null)
            {
                string file = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "effect_end", "e_" + id + ".png");
                if (!File.Exists(file)) { Sheets[key] = null; return null; }
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(File.ReadAllBytes(file))) { Sheets[key] = null; return null; }
                Textures[id] = tex;
            }
            frames = new Sprite[info.n];
            float fh = tex.height / (float)info.n;
            for (int k = 0; k < info.n; k++)
                frames[k] = Sprite.Create(tex, new Rect(0f, tex.height - (k + 1) * fh, tex.width, fh), bottom ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f), 100f);
            Sheets[key] = frames;
            return frames;
        }

        sealed class Run
        {
            public ulong character;
            public uint skill;
            public float start;
            public int gong;                      // ticks of the charge
            public bool hasTarget;
            public float targetX, targetY;        // feet of the target when the skill was used (canvas px)
            public float beamLength = DefaultBeam;              // Nro px (the server tells the real figure when the gong ends: Release)
            public int dir;                                     // the way the server aimed it (1 right, -1 left, 0 = the way the character faces)
            public int activeTicks = 60;                        // ticks the beam hits for (the server: 2 s)
            public float[] spreadX, spreadY, speed;      // the energy balls of Cađíc: where each one lands (Nro px around the target) and how fast it flies
            public readonly List<Image> pool = new List<Image>();
            public int used;
            public RectTransform beamRoot;
            public readonly List<Image> beamPool = new List<Image>();
            public int beamUsed;
            public bool done;
        }

        readonly List<Run> _runs = new List<Run>();

        /// <summary>An ultimate starts charging now: {@code seconds} of charge, then the skill itself. {@code target} is the feet of the enemy it is aimed at.</summary>
        public void Cast(ulong character, uint skillId, float seconds, bool hasTarget, Vector2 target, int dir = 0)
        {
            if (skillId < 24 || skillId > 26) return;
            _runs.RemoveAll(r => { if (r.character == character) { Clear(r); return true; } return false; });
            var run = new Run { dir = dir, character = character, skill = skillId, start = Time.unscaledTime, gong = Mathf.RoundToInt(seconds * 30f), hasTarget = hasTarget, targetX = target.x, targetY = target.y };
            if (skillId == 25)
            {
                var rnd = new System.Random((int)(character * 31 + 7));
                run.spreadX = new float[15];
                run.spreadY = new float[15];
                run.speed = new float[15];
                for (int i = 0; i < 15; i++)
                {
                    run.spreadX[i] = rnd.Next(-20, 21);
                    run.spreadY[i] = rnd.Next(-20, 21);
                    run.speed[i] = rnd.Next(9, 13);
                }
                run.spreadX[14] = run.spreadY[14] = 0f;
                run.speed[14] = 9f;
            }
            _runs.Add(run);
            Sounds(skillId, seconds);
        }

        /// <summary>The gong is over (the server's ULTIMATE_BEAM): the beam goes {@code lengthNro} Nro px and hits for {@code seconds}, through everything in its way.</summary>
        public void Release(ulong character, uint skillId, float lengthNro, float seconds)
        {
            foreach (var r in _runs)
                if (r.character == character && r.skill == skillId)
                {
                    if (lengthNro > 100f) r.beamLength = lengthNro;
                    if (seconds > 0.2f) r.activeTicks = Mathf.RoundToInt(seconds * 30f);
                }
        }

        /// <summary>The ultimate of this character is broken (a stun): what it draws goes away.</summary>
        public void Cancel(ulong character)
        {
            _runs.RemoveAll(r => { if (r.character == character) { Clear(r); return true; } return false; });
        }

        public void CancelAll()
        {
            foreach (var r in _runs) Clear(r);
            _runs.Clear();
            foreach (var j in _jars) EndJar(j);
            _jars.Clear();
        }

        // ---- the targets of Ma phong ba (Effect_End.updListObj_Mafuba, then the jar costume of the server's setIsBinh): they spin into the jar, vanish, and are jars for 11 s

        sealed class JarT
        {
            public ulong caster;
            public string key;
            public float start;                   // Time.unscaledTime when the gong ended
            public float gather;                  // seconds until it is a jar (the server's 4 s)
            public float until;                   // seconds from start until it is itself again (0 = not told yet)
            public float jarX;                    // where the server put the jar (canvas px): which side of the caster it is on
            public int rank;                      // its place among the targets of the same skill: they sit evenly around the vortex
            public int count;
        }

        readonly List<JarT> _jars = new List<JarT>();
        const int SuckStart = 14, Sucking = 44;                                                      // Effect_End: the stars start to turn at tick 14, the radius 90 shrinks to 0

        /// <summary>This target (a training dummy) is sucked into the jar of {@code caster}: it spins in over {@code gatherSeconds}, is hidden, then drawn as a jar.</summary>
        public void JarTarget(ulong caster, string key, float gatherSeconds, float jarX)
        {
            _jars.RemoveAll(x => { if (x.key == key) { EndJar(x); return true; } return false; });
            var j = new JarT { caster = caster, key = key, start = Time.unscaledTime, gather = gatherSeconds, jarX = jarX };
            int same = 0;
            foreach (var o in _jars) if (o.caster == caster && Mathf.Abs(o.start - j.start) < 0.3f) same++;
            j.rank = same;
            _jars.Add(j);
            foreach (var o in _jars) if (o.caster == caster && Mathf.Abs(o.start - j.start) < 0.3f) o.count = same + 1;
        }

        /// <summary>How long the target stays in the jar world in all (gather and jar): after it the dummy is itself again.</summary>
        public void JarTime(string key, float totalSeconds)
        {
            foreach (var j in _jars) if (j.key == key) j.until = totalSeconds;
        }

        void EndJar(JarT j)
        {
            if (village == null) return;
            village.SetDummyJar(j.key, false);
            village.SetDummyDraw(j.key, true, null);
        }

        void TickJars()
        {
            if (_jars.Count == 0) return;
            float s = village.UiScale;
            for (int i = _jars.Count - 1; i >= 0; i--)
            {
                var j = _jars[i];
                float t = Time.unscaledTime - j.start;
                if ((j.until > 0f && t >= j.until) || t > 40f) { EndJar(j); _jars.RemoveAt(i); continue; }
                ulong id;
                Vector2 feet = Vector2.zero, own;
                int facing = 1;
                float h;
                bool found = false;
                for (int k = 0; k < village.PartyCount && !found; k++) found = village.PartyMember(k, out id, out feet, out facing) && id == j.caster;
                if (!found || !village.DummyInfo(j.key, out own, out h)) continue;
                int dir = j.jarX >= feet.x ? 1 : -1;
                int tick = Mathf.FloorToInt(t * 30f);
                if (tick < SuckStart) { village.SetDummyDraw(j.key, true, null); continue; }
                if (tick < SuckStart + Sucking)
                {
                    // spinning in: the angle speeds up by 2 degrees a tick up to 40, the radius (90 Nro px) shrinks by 2 a tick; the targets are spread evenly around the vortex
                    int n = tick - SuckStart + 1;
                    float turned = n <= 20 ? n * (n + 1f) : 420f + 40f * (n - 20);
                    float radius = Mathf.Max(0f, 90f - 2f * n);
                    float angle = (25f + j.rank * (360f / Mathf.Max(1, j.count)) - turned) * Mathf.Deg2Rad;
                    float wispH = Info[24].h / (float)Info[24].n;
                    Vector2 vortex = Nro(feet, dir, 75f, -12f) + new Vector2(0f, (wispH + 50f - 19f) * s);
                    village.SetDummyDraw(j.key, true, vortex + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * s - new Vector2(0f, 20f * s));
                    continue;
                }
                if (t < j.gather) { village.SetDummyDraw(j.key, false, null); continue; }          // inside the jar of the skill, out of sight
                // the target is a jar (Nro: the outfit of the parts 1218 to 1220), on its own feet, until the server says it is itself again
                village.SetDummyJar(j.key, true);
                village.SetDummyDraw(j.key, true, null);
            }
        }

        /// <summary>The sounds of an ultimate (Effect_End: the gong, then the beam, each ball, the jar), started now with their delays.</summary>
        static void Sounds(uint skillId, float gong)
        {
            if (skillId == 24)
            {
                BbSfx.Play(BbSfx.Kamex10Gong, 0.8f);
                BbSfx.Play(BbSfx.Kamex10Beam, 0.9f, gong);
            }
            else if (skillId == 25)
            {
                BbSfx.Play(BbSfx.DestroyGong, 0.8f);
                for (int i = 0; i < 15; i++) BbSfx.Play(i == 14 ? BbSfx.DestroyLast : BbSfx.DestroyBall, 0.7f, gong + 0.01f + i * 0.08f);
            }
            else
            {
                BbSfx.Play(BbSfx.MafubaGong, 0.8f);
                BbSfx.Play(BbSfx.MafubaOpen, 0.8f, gong);
                BbSfx.Play(BbSfx.MafubaVortex, 0.8f, gong + 0.5f);
            }
        }

        void Update()
        {
            Tick();
        }

        /// <summary>For the screenshot tool: every running ultimate shown as if this many ticks had passed.</summary>
        public void DemoAtTick(int tick)
        {
            foreach (var r in _runs) r.start = Time.unscaledTime - (tick + 0.5f) * TickSeconds;
            Tick();
        }

        public void Tick()
        {
            if (village == null || village.WorldLayer == null) return;
            TickJars();
            for (int i = _runs.Count - 1; i >= 0; i--)
            {
                var r = _runs[i];
                ulong id;
                Vector2 feet = Vector2.zero;
                int facing = 1;
                bool found = false;
                for (int k = 0; k < village.PartyCount && !found; k++) found = village.PartyMember(k, out id, out feet, out facing) && id == r.character;
                if (!found) { Clear(r); _runs.RemoveAt(i); continue; }
                int tick = Mathf.FloorToInt((Time.unscaledTime - r.start) / TickSeconds);
                r.used = 0;
                r.beamUsed = 0;
                Draw(r, tick, feet, r.dir != 0 ? r.dir : facing >= 0 ? 1 : -1);
                for (int k = r.used; k < r.pool.Count; k++) if (r.pool[k] != null) r.pool[k].enabled = false;
                for (int k = r.beamUsed; k < r.beamPool.Count; k++) if (r.beamPool[k] != null) r.beamPool[k].enabled = false;
                if (r.beamRoot != null) r.beamRoot.gameObject.SetActive(r.beamUsed > 0);
                if (r.done) { Clear(r); _runs.RemoveAt(i); }
            }
        }

        void Clear(Run r)
        {
            foreach (var img in r.pool) if (img != null) BbFxKill.Go(img.gameObject);
            r.pool.Clear();
            foreach (var img in r.beamPool) if (img != null) BbFxKill.Go(img.gameObject);
            r.beamPool.Clear();
            if (r.beamRoot != null) BbFxKill.Go(r.beamRoot.gameObject);
            r.beamRoot = null;
        }

        // ---------------------------------------------------------------------------------------------------------------- drawing helpers

        Image Take(Run r)
        {
            if (r.used < r.pool.Count && r.pool[r.used] != null) return r.pool[r.used++];
            var go = new GameObject("Ultimate", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(village.WorldLayer, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            if (r.used < r.pool.Count) r.pool[r.used] = img; else r.pool.Add(img);
            r.used++;
            return img;
        }

        /// <summary>One picture of a sheet; {@code bottom}: its bottom centre is on {@code at}, else its centre. Mirrored by {@code flip}, turned by {@code rotation} degrees.</summary>
        Image Put(Run r, int sheet, int frame, bool bottom, Vector2 at, float flip = 1f, float rotation = 0f)
        {
            var frames = Sheet(sheet, bottom);
            if (frames == null) return null;
            var img = Take(r);
            img.enabled = true;
            img.sprite = frames[Mathf.Clamp(frame, 0, frames.Length - 1)];
            var info = Info[sheet];
            var rt = img.rectTransform;
            rt.pivot = bottom ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(info.w, info.h / (float)info.n) * village.UiScale;
            rt.anchoredPosition = at;
            rt.localScale = new Vector3(flip, 1f, 1f);
            rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
            rt.SetAsLastSibling();
            return img;
        }

        Vector2 Nro(Vector2 feet, int dir, float dx, float up)
        {
            float s = village.UiScale;
            return new Vector2(feet.x + dir * dx * s, feet.y + up * s);
        }

        // ---------------------------------------------------------------------------------------------------------------- the three skills

        void Draw(Run r, int tick, Vector2 feet, int dir)
        {
            int[] pow = { 0, 0, 0, 1, 1, 1, 2, 2, 2 };
            int[] powLoop = { 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6 };
            // the charge: the aura (Pow) behind the feet and the sparks (Gong) of the skill
            if (tick < r.gong)
            {
                int auraSheet = r.skill == 24 ? 9 : r.skill == 25 ? 14 : 21;
                int frame = r.skill == 24 ? (tick < pow.Length ? pow[tick] : powLoop[tick % powLoop.Length])
                                          : (tick / 3) % Info[auraSheet].n;
                Put(r, auraSheet, frame, true, Nro(feet, dir, 0f, -13f), dir);
                if (r.skill == 24) Put(r, 10, (tick / 4) % Info[10].n, false, Nro(feet, dir, -3f, Chest), dir);
                else if (r.skill == 25) Put(r, 15, (tick / 4) % Info[15].n, false, Nro(feet, dir, 20f, Chest + 4f), dir);
                else Put(r, 22, (tick / 4) % Info[22].n, false, Nro(feet, dir, 0f, 50f), dir);
            }
            int k = tick - r.gong;                                    // ticks since the skill was released
            if (k < 0) return;
            if (r.skill == 24) DrawBeam(r, k, feet, dir);
            else if (r.skill == 25) DrawBalls(r, k, feet, dir);
            else DrawJar(r, k, feet, dir);
        }

        // ---- 24: Super Kamejoko (Effect_End.upd_Skill_Kamex10, Fill_Rect_Img): the beam grows 20 px a tick to the target, holds, fades

        void DrawBeam(Run r, int k, Vector2 feet, int dir)
        {
            int[] grow = { 0, 0, 0, 1, 1, 1 };
            int[] fade = { 2, 2, 2, 3, 3, 3 };
            int Hold = Mathf.Max(6, r.activeTicks - fade.Length);                  // it hits for the time the server says, then fades
            int frame;
            if (k < Hold) frame = grow[k % 6];
            else if (k - Hold < fade.Length) frame = fade[k - Hold];
            else { r.done = true; return; }
            float s = village.UiScale;
            float w = Mathf.Min(r.beamLength, Mathf.Max(20f, r.beamLength / 24f) * (k + 1));          // 20 px a tick in Nro; a longer beam is as fast as it must be to be across in 0.8 s
            float h = Info[11].h / (float)Info[11].n;
            if (r.beamRoot == null)
            {
                var go = new GameObject("Beam", typeof(RectTransform), typeof(RectMask2D));
                go.transform.SetParent(village.WorldLayer, false);
                r.beamRoot = (RectTransform)go.transform;
                r.beamRoot.anchorMin = r.beamRoot.anchorMax = Vector2.zero;
                r.beamRoot.pivot = new Vector2(0f, 0.5f);
            }
            r.beamRoot.SetAsLastSibling();
            r.beamRoot.anchoredPosition = Nro(feet, dir, 10f, Chest);
            r.beamRoot.sizeDelta = new Vector2(w * s, h * s);
            r.beamRoot.localScale = new Vector3(dir, 1f, 1f);          // the beam is built facing right and turned as a whole
            float headW = Info[11].w, bodyW = Info[12].w, footW = Info[13].w;
            float middle = r.beamLength - headW - footW;
            int tiles = middle > 0f ? Mathf.CeilToInt(middle / bodyW) : 0;
            for (int i = 0; i < tiles; i++)
            {
                float x = i == tiles - 1 ? r.beamLength - bodyW - footW : headW + i * bodyW;
                BeamPiece(r, 12, frame, x);
            }
            BeamPiece(r, 11, frame, 0f);
            BeamPiece(r, 13, frame, r.beamLength - footW - 1f);
        }

        void BeamPiece(Run r, int sheet, int frame, float xNro)
        {
            var frames = Sheet(sheet, false);
            if (frames == null) return;
            Image img;
            if (r.beamUsed < r.beamPool.Count && r.beamPool[r.beamUsed] != null) img = r.beamPool[r.beamUsed];
            else
            {
                var go = new GameObject("BeamPiece", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(r.beamRoot, false);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
                var rt0 = img.rectTransform;
                rt0.anchorMin = rt0.anchorMax = new Vector2(0f, 0.5f);
                rt0.pivot = new Vector2(0f, 0.5f);
                if (r.beamUsed < r.beamPool.Count) r.beamPool[r.beamUsed] = img; else r.beamPool.Add(img);
            }
            r.beamUsed++;
            img.enabled = true;
            img.sprite = frames[Mathf.Clamp(frame, 0, frames.Length - 1)];
            var info = Info[sheet];
            img.rectTransform.sizeDelta = new Vector2(info.w, info.h / (float)info.n) * village.UiScale;
            img.rectTransform.anchoredPosition = new Vector2(xNro * village.UiScale, 0f);
        }

        // ---- 25: Cađíc liên hoàn chưởng (Effect_End.set_Skill_Destroy): 15 balls, one every 80 ms, each flies to the target and explodes; the last is the big one

        void DrawBalls(Run r, int k, Vector2 feet, int dir)
        {
            const int Balls = 15;
            float spacing = 2.4f;                                      // ticks between two balls (80 ms)
            float s = village.UiScale;
            Vector2 from = Nro(feet, dir, 20f, Chest);
            bool any = false;
            for (int i = 0; i < Balls; i++)
            {
                float t = k - i * spacing;
                if (t < 0f) { any = true; continue; }
                bool last = i == Balls - 1;
                Vector2 to = r.hasTarget ? new Vector2(r.targetX + r.spreadX[i] * s, r.targetY + Chest * s + r.spreadY[i] * s) : from + new Vector2(dir * 300f * s, 0f);
                float distance = Vector2.Distance(from, to) / s;           // Nro px
                float flight = distance / r.speed[i];                      // ticks
                if (t < 16f) Put(r, 16, Mathf.FloorToInt(t / 4f), false, from, dir);                // the flash where the ball leaves
                if (t < flight)
                {
                    any = true;
                    Vector2 at = Vector2.Lerp(from, to, t / Mathf.Max(0.01f, flight));
                    Vector2 d = to - from;
                    float angle = Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg * (d.x >= 0f ? 1f : -1f);
                    Put(r, last ? 18 : 17, Mathf.FloorToInt(t / 2f) % Info[last ? 18 : 17].n, false, at, d.x >= 0f ? 1f : -1f, angle);
                }
                else
                {
                    float e = t - flight;
                    int frames = Info[last ? 20 : 19].n;
                    if (e < frames * 4)
                    {
                        any = true;
                        Put(r, last ? 20 : 19, Mathf.FloorToInt(e / 4f), false, to, dir);
                    }
                }
            }
            if (!any) r.done = true;
        }

        // ---- 26: Ma phong ba (Effect_End.set_Skill_MaFuba, upd_Skill_MaFuba): jar opens, vortex above, wisp falls into the jar, the jar shakes, smoke

        void DrawJar(Run r, int k, Vector2 feet, int dir)
        {
            int[] open = { 0, 0, 0, 1, 1, 1, 2, 2, 2 };
            int[] wisp = { 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 2 };
            int[] close = { 3, 3, 3, 3, 3, 4, 4, 4, 5, 5, 5 };
            int[] shake = { 2, 2, 1, 1, 0, 0, 3, 3, 3, 0, 0, 0, 4, 4, 4, 0, 0 };
            int[] shakeLoop = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 0, 0, 0, 4, 4, 4, 0, 0, 0, 0 };
            const int Sucking = 44;                  // ticks the vortex pulls (Nro: 90 and a radius shrinking by 1; here by 2)
            const int Hold = 130;                    // the jar is closed until here (the server holds the targets 4 s)
            float s = village.UiScale;
            Vector2 jar = Nro(feet, dir, 75f, -12f);               // the server's jar point: 75 Nro px in front of the caster (NewSkill._xObjTaget = 75)
            float wispH = Info[24].h / (float)Info[24].n;
            float h = wispH + 50f - 19f;             // the height of the vortex above the jar (Nro: h)
            Vector2 vortex = jar + new Vector2(0f, h * s);
            // the white smoke where the jar appears
            if (k < 24) Put(r, 28, k / 4, true, jar);
            if (k < Sucking + 14 + 10 && k >= 14) Put(r, 23, (k / 4) % Info[23].n, false, vortex);
            int t1 = Sucking + 14;                   // the wisp starts to fall
            int jarFrame;
            if (k < t1)
            {
                jarFrame = open[Mathf.Min(k, open.Length - 1)];
            }
            else if (k < t1 + wisp.Length + 18)
            {
                jarFrame = 2;
                int j = k - t1;
                float vy = 0f, v1 = 1f;
                if (j >= wisp.Length)
                {
                    for (int m = wisp.Length; m <= j; m++) { if (m % 2 == 0) v1++; vy += v1; }
                    vy = Mathf.Min(vy, h - wispH - 25f + 19f);
                }
                int f = wisp[Mathf.Min(j, wisp.Length - 1)];
                bool centred = f == 0 || f == 1;
                Put(r, 24, f, !centred, vortex + new Vector2(0f, (centred ? 0f : 25f * s) - vy * s));
            }
            else if (k < t1 + wisp.Length + 18 + close.Length)
            {
                jarFrame = 2;
                int j = k - (t1 + wisp.Length + 18);
                Put(r, 24, close[j], true, jar + new Vector2(0f, 25f * s));
            }
            else
            {
                int j = k - (t1 + wisp.Length + 18 + close.Length);
                jarFrame = j < shake.Length ? shake[j] : shakeLoop[(j - shake.Length) % shakeLoop.Length];
            }
            if (k < Hold) Put(r, 25, jarFrame, true, jar);
            else
            {
                // the jar opens and the smoke puffs away
                int e = k - Hold;
                if (e < 24) { Put(r, 25, 2, true, jar); Put(r, 28, e / 4, true, jar); }
                else r.done = true;
            }
        }

    }
}
