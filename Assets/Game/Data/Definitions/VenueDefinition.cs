using UnityEngine;
using PWManager.Domain.Models;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Venue")]
    public sealed class VenueDefinition : StaticDefinition
    {
        public VenueScale Scale;
        [Min(1)] public int Capacity = 1;
        [Min(0)] public long RequiredPrestige;
        [Min(0)] public long UnlockCost;
        [Min(0)] public long ProductionCost;
        [Min(1)] public long BaseTicketPrice = 1;
        public string RegionId;
    }
}
