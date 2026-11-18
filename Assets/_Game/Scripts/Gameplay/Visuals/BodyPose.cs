using UnityEngine;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>What a <see cref="CharacterBody"/> should be doing this frame. Fed by players, bots and the lobby preview.</summary>
    public struct BodyPose
    {
        /// <summary>Movement in the body's own frame (m/s; x right, z forward). Drives the walk cycle.</summary>
        public Vector3 LocalVelocity;

        /// <summary>0 standing, 1 crouched.</summary>
        public float Crouch01;

        /// <summary>Crouched head height as a share of standing (the stance's hitbox ratio). 0 uses the default.</summary>
        public float CrouchHeadRatio;

        /// <summary>Look pitch in degrees, positive up.</summary>
        public float Pitch;

        /// <summary>0 weapon at the ready, 1 aimed (shouldered, sights up).</summary>
        public float Aim01;

        /// <summary>Sprinting: weapon carried across the chest.</summary>
        public bool Sprinting;

        /// <summary>Hands busy (reloading, treating a wound): weapon lowered.</summary>
        public bool Lowered;
    }

    /// <summary>How a body is drawn.</summary>
    public enum BodyRenderMode
    {
        /// <summary>Drawn and casting shadows (others' bodies, the preview).</summary>
        Visible = 0,

        /// <summary>Invisible, but still casts a shadow (the owner's own body in first person).</summary>
        ShadowsOnly = 1,

        /// <summary>Not drawn at all (dead: the corpse takes over).</summary>
        Hidden = 2,
    }
}
