using PWManager.Domain.Models;
using SaintsField;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Staff Department")]
    public sealed class StaffDepartmentDefinition : StaticDefinition
    {
        [EnumToggleButtons] public StaffDepartmentType DepartmentType;
        public int StartingLevel;
    }
}
