namespace PWManager.Domain.Services
{
    public readonly struct WrestlingStyleWeights
    {
        public readonly float Brawling;
        public readonly float Power;
        public readonly float HighFlying;
        public readonly float Technical;
        public readonly float BrawlingGrowthBonus, PowerGrowthBonus, HighFlyingGrowthBonus, TechnicalGrowthBonus;

        public WrestlingStyleWeights(float brawling, float power, float highFlying, float technical,
            float brawlingGrowthBonus = 0, float powerGrowthBonus = 0, float highFlyingGrowthBonus = 0, float technicalGrowthBonus = 0)
        {
            Brawling = brawling;
            Power = power;
            HighFlying = highFlying;
            Technical = technical;
            BrawlingGrowthBonus = brawlingGrowthBonus; PowerGrowthBonus = powerGrowthBonus;
            HighFlyingGrowthBonus = highFlyingGrowthBonus; TechnicalGrowthBonus = technicalGrowthBonus;
        }
    }

    public interface IWrestlingStyleRules
    {
        bool TryGetStyleWeights(string styleId, out WrestlingStyleWeights weights);
    }
}
