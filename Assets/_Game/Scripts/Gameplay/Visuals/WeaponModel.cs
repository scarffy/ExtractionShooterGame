using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// A built primitive weapon (see <see cref="WeaponModelBuilder"/>) and the points the body and the first-person view
    /// need from it. Local frame: the barrel points along +Z, up is +Y, the origin is the pistol grip.
    /// <list type="bullet">
    /// <item><see cref="Grip"/>: where the firing hand holds it. <see cref="Support"/>: where the other hand goes.</item>
    /// <item><see cref="Sight"/>: the rear of the line of sight (rear sight or optic). The line of sight runs along +Z
    /// through it, so aiming puts the eye <see cref="EyeRelief"/> behind it.</item>
    /// <item><see cref="Muzzle"/>: end of the barrel or muzzle device; the flash plays there.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponModel : MonoBehaviour
    {
        private readonly List<Renderer> _flashRenderers = new List<Renderer>();

        private Transform _flash;
        private float _flashUntil;
        private bool _flashAllowed = true;

        public GearShape Shape { get; private set; }
        public HoldStyle Hold { get; private set; }
        public Transform Grip { get; private set; }
        public Transform Support { get; private set; }
        public Transform Sight { get; private set; }
        public Transform Muzzle { get; private set; }

        /// <summary>Metres between the eye and <see cref="Sight"/> when aiming down sights.</summary>
        public float EyeRelief { get; private set; }

        /// <summary>The sight is a magnified scope (the HUD's scope overlay takes over when fully aimed).</summary>
        public bool Scoped { get; private set; }

        /// <summary>Middle of the weapon (local), for slinging it on the back.</summary>
        public Vector3 Center { get; private set; }

        internal void Configure(GearShape shape, Transform grip, Transform support, Transform sight, Transform muzzle,
            float eyeRelief, bool scoped, Vector3 center, Transform flash)
        {
            Shape = shape;
            Hold = VisualClassifier.HoldOf(shape);
            Grip = grip;
            Support = support;
            Sight = sight;
            Muzzle = muzzle;
            EyeRelief = eyeRelief;
            Scoped = scoped;
            Center = center;
            _flash = flash;
            _flashRenderers.Clear();
            if (_flash != null)
            {
                _flash.GetComponentsInChildren(true, _flashRenderers);
                foreach (Renderer flashRenderer in _flashRenderers)
                {
                    flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    flashRenderer.receiveShadows = false;
                }
                _flash.gameObject.SetActive(false);
            }
        }

        /// <summary>The flash renderers (kept out of shadows).</summary>
        public IReadOnlyList<Renderer> FlashRenderers => _flashRenderers;

        /// <summary>Whether flashes may show (off while the body is only casting shadows).</summary>
        public void SetFlashAllowed(bool allowed)
        {
            _flashAllowed = allowed;
            if (!allowed && _flash != null) _flash.gameObject.SetActive(false);
        }

        /// <summary>Shows the muzzle flash for <paramref name="seconds"/>, with a random roll and size.</summary>
        public void Flash(float seconds)
        {
            if (_flash == null || !_flashAllowed) return;
            _flash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.8f, 1.25f);
            _flash.localScale = new Vector3(size, size, Random.Range(0.85f, 1.4f));
            _flash.gameObject.SetActive(true);
            _flashUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (_flash != null && _flash.gameObject.activeSelf && Time.time >= _flashUntil)
                _flash.gameObject.SetActive(false);
        }
    }
}
