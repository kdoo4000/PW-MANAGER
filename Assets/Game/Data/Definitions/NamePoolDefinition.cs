using System.Collections.Generic;
using SaintsField.Playa;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Name Pool")]
    public sealed class NamePoolDefinition : ScriptableObject
    {
        [ListDrawerSettings] public List<string> MaleGivenNames = new();
        [ListDrawerSettings] public List<string> FemaleGivenNames = new();
        [ListDrawerSettings] public List<string> FamilyNames = new();
        [ListDrawerSettings] public List<string> KoreanMaleGivenNames = new();
        [ListDrawerSettings] public List<string> KoreanFemaleGivenNames = new();
        [ListDrawerSettings] public List<string> KoreanFamilyNames = new();
        [ListDrawerSettings] public List<string> JapaneseMaleGivenNames = new();
        [ListDrawerSettings] public List<string> JapaneseFemaleGivenNames = new();
        [ListDrawerSettings] public List<string> JapaneseFamilyNames = new();
    }
}
