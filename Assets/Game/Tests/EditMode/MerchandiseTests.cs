using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;
namespace PWManager.Tests
{
    public sealed class MerchandiseTests
    {
        [Test]
        public void DailyFlowAndSaveService_PreserveSalesAndDoNotPayAgainOnLoad()
        {
            var save=SponsorMissionTests.CreateSave();
            foreach(var schedule in save.Schedules) schedule.Status=ScheduleStatus.Cancelled;
            foreach(var w in save.Wrestlers) { w.Fame=WrestlerFameRules.Create(500);w.FanReaction.UsesTwoAxes=true;w.FanReaction.Mark=w.FanReaction.Casual=w.FanReaction.Hardcore=new FanResponseState{Interest=50,Preference=50}; }
            var result=new TimeFlowService().AdvanceTo(save,save.CurrentDate.AddDays(7));
            Assert.That(result.ProcessedDays,Is.EqualTo(7));
            Assert.That(save.Merchandise.Sales.Count,Is.EqualTo(8));
            Assert.That(save.Merchandise.Sales.Sum(x=>x.Net),Is.EqualTo(960));
            var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"pw-merch-"+Guid.NewGuid());
            try {
                var storage=new SaveService(path);storage.Save("merch",save);var restored=storage.Load("merch");
                var cash=restored.Promotion.CalculateCurrentCash(restored.Transactions);new MerchandiseService().ProcessDay(restored);
                Assert.That(restored.Promotion.CalculateCurrentCash(restored.Transactions),Is.EqualTo(cash));
                Assert.That(restored.Merchandise.Sales.Count,Is.EqualTo(8));MerchandiseService.ValidateState(restored);
            } finally {if(System.IO.Directory.Exists(path))System.IO.Directory.Delete(path,true);}
        }

        private static GameSave Sample(long fans = 2500)
        {
            var save = new GameSave { CurrentDate = new GameDate(2026,6,1), Promotion = new PromotionState { Id = "p", InitialCash = 10000, Audience = FanAudienceService.CreateInitial(new GameDate(2026,6,1), fans) } };
            var w = new WrestlerState { Id = "w", PromotionId = "p" }; w.Roster.ActivityState = RosterActivityState.Active;
            w.Fame = WrestlerFameRules.Create(500); w.FanReaction.UsesTwoAxes = true;
            w.FanReaction.Mark = w.FanReaction.Casual = w.FanReaction.Hardcore = new FanResponseState { Interest = 50, Preference = 50 };
            save.Wrestlers.Add(w); save.Contracts.Add(new ContractState { Id = "c", PersonId = "w", Type = ContractType.Wrestler,
                Status = ContractStatus.Active, StartDate = save.CurrentDate, EndDate = new GameDate(2027,5,31) });
            MerchandiseService.Initialize(save); return save;
        }
        [Test]
        public void Online_ApprovedExamplePaysGrossCostsAndRoyaltyOnceAfterSevenDays()
        {
            var save = Sample(); var service = new MerchandiseService();
            save.CurrentDate = save.CurrentDate.AddDays(6); service.ProcessDay(save); Assert.That(save.Transactions, Is.Empty);
            save.CurrentDate = save.CurrentDate.AddDays(1); service.ProcessDay(save);
            var sale = save.Merchandise.Sales.Single(); Assert.That(sale.Units, Is.EqualTo(10)); Assert.That(sale.Net, Is.EqualTo(120));
            Assert.That(save.Transactions.Select(x => x.Amount), Is.EquivalentTo(new long[]{300,-80,-40,-60}));
            var before = JsonUtility.ToJson(save); service.ProcessDay(save); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            MerchandiseService.ValidateState(save);
        }
        [Test]
        public void FractionalSales_PersistSeparatelyAndAccumulateAcrossLoad()
        {
            var save = Sample(125); save.Wrestlers[0].Fame.Value = 0;
            for(int i=0;i<4;i++) {save.CurrentDate=save.CurrentDate.AddDays(7);new MerchandiseService().ProcessDay(save); save=JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));}
            Assert.That(save.Merchandise.Sales.Single().Units, Is.EqualTo(1));
            Assert.That(save.Merchandise.Remainders.Single().Online, Is.EqualTo(0).Within(.000001));
            Assert.That(save.Merchandise.Remainders.Single().Venue, Is.Zero); MerchandiseService.ValidateState(save);
        }
        [Test]
        public void Reaction_UsesFanWeightsAndDoesNotPenalizeAlignment()
        {
            var save=Sample();var w=save.Wrestlers[0];
            Assert.That(MerchandiseService.Reaction(save,w),Is.EqualTo(1));
            w.Roster.Alignment=KayfabeAlignment.Heel; Assert.That(MerchandiseService.Reaction(save,w),Is.EqualTo(1));
            w.FanReaction.Mark=w.FanReaction.Casual=w.FanReaction.Hardcore=new FanResponseState{Interest=100,Preference=0};
            Assert.That(MerchandiseService.Reaction(save,w),Is.EqualTo(1.5));
        }
        [Test]
        public void InjuredRosterSellsOnlineButDepartedAndExpiredContractsDoNot()
        {
            var save=Sample();save.Wrestlers[0].Condition.InjuryStatus=InjuryStatus.Severe;
            save.CurrentDate=save.CurrentDate.AddDays(7);new MerchandiseService().ProcessDay(save);Assert.That(save.Merchandise.Sales.Count,Is.EqualTo(1));
            save.Contracts[0].Status=ContractStatus.Expired;save.CurrentDate=save.CurrentDate.AddDays(7);new MerchandiseService().ProcessDay(save);
            Assert.That(save.Merchandise.Sales.Count,Is.EqualTo(1));
        }
        [Test]
        public void Migration_StartsAtCurrentDateWithoutRetroactiveMoney()
        {
            var save=Sample();save.CurrentDate=new GameDate(2026,12,1);save.SaveVersion=18;var cash=save.Promotion.CalculateCurrentCash(save.Transactions);
            new SaveVersion18To19Migration().Migrate(save);new MerchandiseService().ProcessDay(save);
            Assert.That(save.SaveVersion,Is.EqualTo(19)); Assert.That(save.Merchandise.Sales,Is.Empty);Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions),Is.EqualTo(cash));
        }
        [Test]
        public void ActualShow_UsesAttendanceAndTimeWeightedAppearanceAndSettlesOnce()
        {
            var save=(GameSave)typeof(ShowResultApplicationServiceTests).GetMethod("CreateSave",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            save.CurrentDate=save.Shows[0].Date;save.Promotion.Audience=FanAudienceService.CreateInitial(save.CurrentDate);
            foreach(var w in save.Wrestlers) {w.PromotionId=save.Promotion.Id;w.Roster.ActivityState=RosterActivityState.Active;w.Fame=WrestlerFameRules.Create(500);w.FanReaction.UsesTwoAxes=true;w.FanReaction.Mark=w.FanReaction.Casual=w.FanReaction.Hardcore=new FanResponseState{Interest=50,Preference=50};save.Contracts.Add(new ContractState{Id="contract-"+w.Id,PersonId=w.Id,Type=ContractType.Wrestler,Status=ContractStatus.Active,StartDate=save.CurrentDate,EndDate=save.CurrentDate.AddDays(30)});}
            save.ShowEvents[0].PlannedDuration=10;save.ShowEvents[1].PlannedDuration=30;
            save.MatchResults[0].FinalMatchQuality=16;save.PromoResults[0].PromoScore=20;
            save.ShowResults[0].FinancialSettlement.Attendance=500;save.ShowResults[0].FinancialSettlement.TicketPrice=2;
            var before=JsonUtility.ToJson(save);var prepared=new MerchandiseService().PrepareShow(ResultApplicationContext.Create(save,"show_result"));
            Assert.That(JsonUtility.ToJson(save),Is.EqualTo(before));
            new ShowResultApplicationService().Apply(save,"show_result");
            Assert.That(save.Merchandise.Sales.Single(x=>x.WrestlerId=="a").Units,Is.EqualTo(4));
            Assert.That(save.Merchandise.Remainders.Single(x=>x.WrestlerId=="a").Venue,Is.EqualTo(.625).Within(.00001));
            Assert.That(save.Merchandise.Sales.Single(x=>x.WrestlerId=="b").Units,Is.EqualTo(5));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions),Is.EqualTo(600+9*14));
            var after=JsonUtility.ToJson(save);Assert.Throws<InvalidOperationException>(()=>new ShowResultApplicationService().Apply(save,"show_result"));Assert.That(JsonUtility.ToJson(save),Is.EqualTo(after));
            MerchandiseService.ValidateState(save);
        }
        [Test]
        public void CorruptRemainderOrLedgerIsRejected()
        {
            var save=Sample();save.CurrentDate=save.CurrentDate.AddDays(7);new MerchandiseService().ProcessDay(save);
            save.Transactions[0].Amount++;Assert.Throws<InvalidOperationException>(()=>MerchandiseService.ValidateState(save));save.Transactions[0].Amount--;
            save.Merchandise.Remainders[0].Online=double.NaN;Assert.Throws<InvalidOperationException>(()=>MerchandiseService.ValidateState(save));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void LedgerLookup_RejectsActiveDuplicatesAndIgnoresVoidedDuplicates(bool voided)
        {
            var save=Sample();save.CurrentDate=save.CurrentDate.AddDays(7);new MerchandiseService().ProcessDay(save);
            var entry=save.Transactions.First(x=>x.Type==TransactionType.MerchandiseRevenue);
            save.Transactions.Add(new TransactionRecord {Id=Guid.NewGuid().ToString(),PromotionId=entry.PromotionId,Date=entry.Date,
                Type=entry.Type,Amount=entry.Amount,ReasonId=entry.ReasonId,IsVoided=voided});
            if(voided) Assert.DoesNotThrow(()=>MerchandiseService.ValidateState(save));
            else Assert.Throws<InvalidOperationException>(()=>MerchandiseService.ValidateState(save));
        }
    }
}
