namespace PWManager.Domain.Services
{
    public readonly struct WrestlingStyleWeights
    {
        public readonly float Brawling;
        public readonly float Power;
        public readonly float HighFlying;
        public readonly float Technical;

        public WrestlingStyleWeights(float brawling, float power, float highFlying, float technical)
        {
            Brawling = brawling;
            Power = power;
            HighFlying = highFlying;
            Technical = technical;
        }
    }

    public interface IWrestlingStyleRules
    {
        bool TryGetStyleWeights(string styleId, out WrestlingStyleWeights weights);
    }
}
