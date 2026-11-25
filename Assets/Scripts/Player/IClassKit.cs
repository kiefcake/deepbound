using System.Collections.Generic;
using Deepbound.Classes;
using UnityEngine;

namespace Deepbound.Player
{
    public struct AbilityView
    {
        public string Key;
        public string Name;

        public float Remaining;
        public float Cooldown;

        public bool Active;

        public bool Unaffordable;
    }

    public interface IClassKit
    {
        ClassId Class { get; }
        string ResourceName { get; }
        Color ResourceColor { get; }
        float Resource { get; }
        float ResourceMax { get; }
        void FillAbilities(List<AbilityView> into);

        string ResourceHint => "";

        float Charge => 0f;

        float AttackRange => 0f;

        void RestoreResource(float fraction);

        void ShaveCooldowns(float seconds) { }
    }
}
