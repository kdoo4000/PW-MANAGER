using PWManager.Domain.Models;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Staff Department")]
    public sealed class StaffDepartmentDefinition : StaticDefinition
    {
        public StaffDepartmentType DepartmentType;
        public int StartingLevel;
    }
}
