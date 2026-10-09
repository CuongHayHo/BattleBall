using System;
using System.Collections.Generic;
using System.IO;
using NroRig;
using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// The sound effects of the skills (owner request 2026-10-09): the files of res/sound of Nro, numbered like SoundMn of the Nro client (data/sound/{id}.wav in the
    /// asset packs). A clip is read from the downloaded folder the first time it is needed and kept; a small pool of audio sources plays them, so that several
    /// sounds can overlap. Nothing happens outside play mode or when the file is missing.
    /// </summary>
    public sealed class BbSfx : MonoBehaviour
    {
        // the numbers of SoundMn
        public const int Move = 1, Fly = 4, LowPunch = 2, LowKick = 3, MediumPunch = 9, MediumKick = 10, NamekKame = 15, XaydaKame = 16, Explode1 = 17, HpUp = 20, EarthKame = 19,
                         SolarFlare = 21, Revive = 22, Gong = 23, BigExplode = 25, NamekLazer = 26, NamekCharge = 27, Kamex10Gong = 31, Kamex10Beam = 32,
                         DestroyBall = 33, DestroyLast = 34, MafubaOpen = 35, MafubaVortex = 36, MafubaGong = 37, DestroyGong = 38;

        const int Voices = 24;
        const string PrefKey = "bb_sound";

        /// <summary>The player's choice (on by default), kept between runs; switching it off silences what is playing.</summary>
        public static bool Enabled
        {
            get { try { return PlayerPrefs.GetInt(PrefKey, 1) != 0; } catch (Exception) { return true; } }
            set
            {
                try { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); } catch (Exception) { }
                if (!value && _instance != null && _instance._voices != null) foreach (var v in _instance._voices) v.Stop();
            }
        }

        static BbSfx _instance;
        static readonly Dictionary<int, AudioClip> Clips = new Dictionary<int, AudioClip>();
        static readonly HashSet<int> Missing = new HashSet<int>();
        static readonly Dictionary<int, float> LastPlayed = new Dictionary<int, float>();

        AudioSource[] _voices;
        int _next;

        static BbSfx Instance()
        {
            if (!Application.isPlaying) return null;
            if (_instance != null) return _instance;
            var go = new GameObject("BbSfx");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BbSfx>();
            _instance._voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _instance._voices[i] = source;
            }
            return _instance;
        }

        /// <summary>Plays sound {@code id} of Nro at this volume after {@code delay} seconds (the same sound is not started twice within 60 ms).</summary>
        public static void Play(int id, float volume, float delay = 0f)
        {
            if (!Enabled) return;
            var self = Instance();
            if (self == null) return;
            float last;
            if (delay <= 0f && LastPlayed.TryGetValue(id, out last) && Time.unscaledTime - last < 0.06f) return;
            LastPlayed[id] = Time.unscaledTime;
            var clip = Load(id);
            if (clip == null) return;
            var source = self._voices[self._next++ % Voices];
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            if (delay > 0f) source.PlayDelayed(delay); else source.Play();
        }

        static AudioClip Load(int id)
        {
            AudioClip clip;
            if (Clips.TryGetValue(id, out clip)) return clip;
            if (Missing.Contains(id)) return null;
            try
            {
                string file = Path.Combine(NroSpriteResolver.GetDefaultServerRoot(), "data", "sound", id + ".wav");
                if (File.Exists(file)) clip = Parse(id, File.ReadAllBytes(file));
            }
            catch (Exception) { clip = null; }
            if (clip == null) Missing.Add(id); else Clips[id] = clip;
            return clip;
        }

        /// <summary>A 16-bit PCM wave file (what Nro has) into a clip.</summary>
        static AudioClip Parse(int id, byte[] b)
        {
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return null;
            int channels = 0, rate = 0, bits = 0, dataAt = -1, dataLength = 0;
            int p = 12;
            while (p + 8 <= b.Length)
            {
                string tag = "" + (char)b[p] + (char)b[p + 1] + (char)b[p + 2] + (char)b[p + 3];
                int length = BitConverter.ToInt32(b, p + 4);
                if (tag == "fmt ")
                {
                    channels = BitConverter.ToInt16(b, p + 10);
                    rate = BitConverter.ToInt32(b, p + 12);
                    bits = BitConverter.ToInt16(b, p + 22);
                }
                else if (tag == "data") { dataAt = p + 8; dataLength = Math.Min(length, b.Length - dataAt); break; }
                p += 8 + length + (length & 1);
            }
            if (dataAt < 0 || bits != 16 || channels < 1 || rate <= 0) return null;
            int samples = dataLength / 2;
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = BitConverter.ToInt16(b, dataAt + i * 2) / 32768f;
            var clip = AudioClip.Create("nro_sound_" + id, samples / channels, channels, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
