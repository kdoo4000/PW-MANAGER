using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class StaffDepartmentState
    {
        public string Id;
        public StaffDepartmentType DepartmentType;
        public int CurrentLevel;
        public int UnlockedLevel;
    }
}
