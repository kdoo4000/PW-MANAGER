using System.Collections.Generic;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    public enum TraitPolarity { Positive, Negative, Mixed }

    [CreateAssetMenu(menuName = "PW Manager/Static/Trait")]
    public sealed class TraitDefinition : StaticDefinition
    {
        public TraitPolarity Polarity;
        [TextArea] public string Description;
        public List<string> ConflictingTraitIds = new();
    }
}
