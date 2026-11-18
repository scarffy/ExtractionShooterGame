using System.Collections.Generic;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Audio
{
    /// <summary>The kinds of gun report, from a pistol crack to a magnum boom.</summary>
    public enum GunSound
    {
        Pistol,
        Smg,
        Rifle,
        Battle,
        Shotgun,
        Magnum,
    }

    /// <summary>
    /// Placeholder sounds synthesised at runtime (noise bursts, thumps, clicks), so the game has audio before real
    /// recordings are imported. Each clip is built once and cached. Swap in real clips by assigning
    /// <see cref="CharacterAudio"/> overrides later; nothing else depends on these being procedural.
    /// </summary>
    public static class ProceduralAudio
    {
        private const int SampleRate = 44100;
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static GunSound ClassOf(WeaponDefinition weapon)
        {
            if (weapon == null) return GunSound.Rifle;
            switch (weapon.Caliber)
            {
                case Caliber.Mm9x19:
                case Caliber.Acp45:
                    return weapon.WeaponClass == WeaponClass.Pistol ? GunSound.Pistol : GunSound.Smg;
                case Caliber.Mm46x30: return GunSound.Smg;
                case Caliber.Ae50: return GunSound.Magnum;
                case Caliber.Gauge12: return GunSound.Shotgun;
                case Caliber.Mm762x51:
                case Caliber.Mm762x54R: return GunSound.Battle;
                case Caliber.Lm338: return GunSound.Magnum;
                default: return GunSound.Rifle;
            }
        }

        /// <summary>How far a gun class carries (metres), for 3D rolloff.</summary>
        public static float GunRange(GunSound sound)
        {
            switch (sound)
            {
                case GunSound.Pistol: return 120f;
                case GunSound.Smg: return 140f;
                case GunSound.Shotgun: return 180f;
                case GunSound.Magnum: return 300f;
                case GunSound.Battle: return 260f;
                default: return 220f;
            }
        }

        public static AudioClip Gunshot(GunSound sound)
        {
            switch (sound)
            {
                // name, length, noise decay, thump Hz, thump amount, crack amount, low-pass (0..1 smoothing)
                case GunSound.Pistol: return Shot("gun_pistol", 0.35f, 22f, 110f, 0.35f, 0.6f, 0.35f);
                case GunSound.Smg: return Shot("gun_smg", 0.3f, 26f, 120f, 0.3f, 0.5f, 0.4f);
                case GunSound.Shotgun: return Shot("gun_shotgun", 0.6f, 12f, 70f, 0.8f, 0.4f, 0.6f);
                case GunSound.Battle: return Shot("gun_battle", 0.6f, 13f, 75f, 0.7f, 0.7f, 0.5f);
                case GunSound.Magnum: return Shot("gun_magnum", 0.8f, 9f, 60f, 0.9f, 0.8f, 0.55f);
                default: return Shot("gun_rifle", 0.5f, 16f, 90f, 0.55f, 0.7f, 0.45f);
            }
        }

        /// <summary>A footstep; <paramref name="variant"/> 0..3 gives slightly different steps.</summary>
        public static AudioClip Footstep(int variant) =>
            Build($"step_{variant}", 0.12f, (t, rng) => Noise(rng) * Mathf.Exp(-t * 45f) * (0.6f + 0.1f * variant), 0.75f, 100 + variant);

        public static AudioClip KnifeSwing() =>
            Build("knife_swing", 0.28f, (t, rng) => Noise(rng) * Mathf.Sin(Mathf.PI * t / 0.28f) * 0.5f, 0.85f, 7);

        public static AudioClip KnifeHit() =>
            Build("knife_hit", 0.18f, (t, rng) => (Noise(rng) * 0.6f + Mathf.Sin(2f * Mathf.PI * 140f * t)) * Mathf.Exp(-t * 30f) * 0.7f, 0.6f, 8);

        public static AudioClip ReloadClick() =>
            Build("reload", 0.5f, (t, rng) => Click(t, 0.02f) + Click(t, 0.32f) * 0.8f, 0.2f, 9);

        /// <summary>A short soft tick for menu buttons.</summary>
        public static AudioClip UiClick() =>
            Build("ui_click", 0.05f, (t, rng) => Click(t, 0f) * 0.5f, 0.3f, 12);

        public static AudioClip Hurt() =>
            Build("hurt", 0.22f, (t, rng) => (Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.8f + Noise(rng) * 0.3f) * Mathf.Exp(-t * 18f), 0.7f, 10);

        /// <summary>Frag grenade: a deep, long boom.</summary>
        public static AudioClip Explosion() =>
            Build("grenade_frag", 1.6f, (t, rng) =>
            {
                float rumble = Noise(rng) * Mathf.Exp(-t * 3.5f);
                float low = Mathf.Sin(2f * Mathf.PI * 45f * t) * Mathf.Exp(-t * 4f) * 0.9f;
                float snap = t < 0.01f ? Noise(rng) * 1.5f : 0f;
                return (rumble + low + snap) * 0.85f;
            }, 0.7f, 11);

        /// <summary>Flashbang: a sharp crack with a high ring.</summary>
        public static AudioClip FlashBang() =>
            Build("grenade_flash", 1.2f, (t, rng) =>
            {
                float crack = Noise(rng) * Mathf.Exp(-t * 14f);
                float ring = Mathf.Sin(2f * Mathf.PI * 3200f * t) * Mathf.Exp(-t * 2.5f) * 0.25f;
                return (crack + ring) * 0.9f;
            }, 0.3f, 12);

        /// <summary>Smoke grenade: a pop, then a hiss.</summary>
        public static AudioClip SmokePop() =>
            Build("grenade_smoke", 1.5f, (t, rng) =>
            {
                float pop = Noise(rng) * Mathf.Exp(-t * 40f);
                float hiss = Noise(rng) * 0.35f * Mathf.Clamp01(t * 6f) * Mathf.Exp(-t * 1.5f);
                return pop + hiss;
            }, 0.15f, 13);

        /// <summary>Squad ping: a short two-note blip (rising for a marker, a lower double beep for danger).</summary>
        public static AudioClip PingBlip(bool danger) => danger
            ? Build("ping_danger", 0.24f, (t, rng) => Tone(t, 0f, 0.09f, 740f) + Tone(t, 0.12f, 0.09f, 740f), 0.1f, 14)
            : Build("ping", 0.2f, (t, rng) => Tone(t, 0f, 0.07f, 880f) + Tone(t, 0.08f, 0.1f, 1320f), 0.1f, 15);

        private static AudioClip Shot(string name, float length, float decay, float thumpHz, float thump, float crack, float lowPass) =>
            Build(name, length, (t, rng) =>
            {
                float body = Noise(rng) * Mathf.Exp(-t * decay);
                float low = Mathf.Sin(2f * Mathf.PI * thumpHz * t) * Mathf.Exp(-t * 18f) * thump;
                float snap = t < 0.006f ? Noise(rng) * crack * 1.5f : 0f;
                return (body + low + snap) * 0.8f;
            }, lowPass, name.GetHashCode());

        private delegate float Synth(float time, System.Random rng);

        private static AudioClip Build(string name, float length, Synth synth, float smoothing, int seed)
        {
            if (Cache.TryGetValue(name, out AudioClip cached) && cached != null) return cached;

            int samples = Mathf.Max(1, Mathf.RoundToInt(length * SampleRate));
            var data = new float[samples];
            var rng = new System.Random(seed);
            float previous = 0f;
            for (int i = 0; i < samples; i++)
            {
                float value = synth(i / (float)SampleRate, rng);
                // One-pole low-pass: smoothing 0 = raw noise, closer to 1 = duller.
                previous = Mathf.Lerp(value, previous, smoothing);
                data[i] = Mathf.Clamp(previous, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            Cache[name] = clip;
            return clip;
        }

        private static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        /// <summary>A sine note from <paramref name="at"/> lasting <paramref name="length"/> seconds, with soft edges.</summary>
        private static float Tone(float t, float at, float length, float hz)
        {
            float local = t - at;
            if (local < 0f || local > length) return 0f;
            float envelope = Mathf.Min(1f, local / 0.008f) * Mathf.Min(1f, (length - local) / 0.03f);
            return Mathf.Sin(2f * Mathf.PI * hz * local) * envelope * 0.35f;
        }

        private static float Click(float t, float at)
        {
            float local = t - at;
            return local < 0f || local > 0.03f ? 0f : Mathf.Sin(2f * Mathf.PI * 1800f * local) * Mathf.Exp(-local * 160f);
        }
    }
}
