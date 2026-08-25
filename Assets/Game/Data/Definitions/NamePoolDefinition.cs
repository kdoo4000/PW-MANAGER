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
        public List<string> Nicknames = new();
        public List<string> SingleWordRingNames = new();
    }
}
