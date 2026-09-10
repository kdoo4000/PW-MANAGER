using System.Collections.Generic;
using SaintsField;
using SaintsField.Playa;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    public enum TraitPolarity { Positive, Negative, Mixed }

    [CreateAssetMenu(menuName = "PW Manager/Static/Trait")]
    public sealed class TraitDefinition : StaticDefinition
    {
        [EnumToggleButtons] public TraitPolarity Polarity;
        [ResizableTextArea] public string Description;
        [ListDrawerSettings] public List<string> ConflictingTraitIds = new();
    }
}
