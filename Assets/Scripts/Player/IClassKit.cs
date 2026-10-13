using System.Collections.Generic;
using Deepbound.Classes;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>One ability slot as the HUD draws it.</summary>
    public struct AbilityView
    {
        public string Key;
        public string Name;
        /// <summary>Seconds until ready; 0 or less when ready.</summary>
        public float Remaining;
        public float Cooldown;
        /// <summary>Highlighted: held (block) or a buff running (War Cry).</summary>
        public bool Active;
        /// <summary>Cannot afford it right now (resource), drawn dimmed.</summary>
        public bool Unaffordable;
    }

    /// <summary>
    /// What every class kit exposes to the rest of the game. Every kit component lives on
    /// the player prefab; only the one matching the player's chosen class acts. Keeping
    /// them all present (rather than adding and removing NetworkBehaviours) keeps the
    /// prefab's network identity fixed, which NGO requires.
    /// </summary>
    public interface IClassKit
    {
        ClassId Class { get; }
        string ResourceName { get; }
        Color ResourceColor { get; }
        float Resource { get; }
        float ResourceMax { get; }
        void FillAbilities(List<AbilityView> into);
    }
}
