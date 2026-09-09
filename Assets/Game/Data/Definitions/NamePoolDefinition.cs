using System.Collections.Generic;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Name Pool")]
    public sealed class NamePoolDefinition : ScriptableObject
    {
        public List<string> MaleGivenNames = new();
        public List<string> FemaleGivenNames = new();
        public List<string> FamilyNames = new();
        public List<string> KoreanMaleGivenNames = new();
        public List<string> KoreanFemaleGivenNames = new();
        public List<string> KoreanFamilyNames = new();
        public List<string> JapaneseMaleGivenNames = new();
        public List<string> JapaneseFemaleGivenNames = new();
        public List<string> JapaneseFamilyNames = new();
    }
}
