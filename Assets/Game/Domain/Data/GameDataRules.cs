namespace PWManager.Domain.Data
{
    public static class GameDataRules
    {
        public const string StaticIdExample = "trait_001";
        public const string RuntimeIdFormat = "GUID D format";

        public static bool IsPersisted(GameDataCategory category)
        {
            return category is GameDataCategory.State or GameDataCategory.Record;
        }

        public static bool IsAuthoredContent(GameDataCategory category)
        {
            return category is GameDataCategory.Static or GameDataCategory.Balance;
        }
    }
}
