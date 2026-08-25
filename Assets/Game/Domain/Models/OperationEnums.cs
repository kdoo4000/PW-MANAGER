namespace PWManager.Domain.Models
{
    public enum StaffDepartmentType { Medical, Scout, Promotion, Commentary }
    public enum VenueContractType { RegularSeason, PpvReservation }
    public enum VenueContractStatus { Draft, Reserved, Paid, Used, Cancelled }
    public enum VenueScale { Studio, CommunityGym, SmallArena, MediumArena, LargeArena, Stadium }
    public enum RegularShowFrequency { Monthly, Biweekly, Weekly, TwiceWeekly, ThreeTimesWeekly }
    public enum PpvFrequency { EveryFourMonths, Quarterly, Bimonthly, Monthly }
    public enum ScheduledShowType { Regular, PpvRegular, PpvMajor, PpvSignature }
    public enum ScheduleStatus { Draft, Confirmed, Completed, Cancelled }
}
