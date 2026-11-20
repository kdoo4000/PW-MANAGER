using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class ChampionshipProfileView
    {
        internal static string DivisionText(ChampionshipDivision division) => division switch
        {
            ChampionshipDivision.Men => "남성부",
            ChampionshipDivision.Women => "여성부",
            _ => "오픈"
        };

        public static Button CreateLink(ChampionshipState title, Action<string> navigate = null)
        {
            var button = new Button(() => { if (navigate != null) navigate(title.Id); else DashboardController.OpenChampionshipProfile(title.Id); }) { text = title.Name };
            button.AddToClassList("championship-link"); button.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            button.SetEnabled(!DashboardSession.IsShowRunning); return button;
        }

        internal static void Gallery(VisualElement root, GameSave save, IReadOnlyList<ChampionshipState> titles, Action<string> navigate)
        {
            var scroll = new ScrollView { name = "championship-gallery" }; scroll.AddToClassList("ops-scroll"); root.Add(scroll);
            scroll.contentContainer.AddToClassList("cp-tier-columns");
            foreach (var (label, category) in new[] { ("1선급", 0), ("2선급", 1), ("태그팀", 2) })
            {
            var tier = titles.Where(t => (t.Type == ChampionshipType.TagTeam ? 2 : t.IsWorldChampionship ? 0 : 1) == category).ToList();
            if (tier.Count == 0) continue;
            var section = new VisualElement { name = "championship-tier-" + category }; section.AddToClassList("cp-tier"); scroll.Add(section);
            var heading = new Label(label); heading.AddToClassList("cp-tier-heading"); section.Add(heading);
            var grid = new VisualElement(); grid.AddToClassList("cp-gallery"); section.Add(grid);
            foreach (var title in tier)
            {
                var card = new VisualElement { name = "championship-select-" + title.Id }; card.AddToClassList("cp-card"); grid.Add(card);
                var pictures = new VisualElement(); pictures.AddToClassList("cp-card-pictures"); card.Add(pictures);
                var members = ChampionshipService.CurrentMemberIds(save, title);
                if (members.Count > 0) pictures.Add(new GroupPortraitView(members.Select(id => save.Wrestlers.FirstOrDefault(w => w.Id == id)), title.Id));
                if (members.Count == 0) pictures.Add(ManagementVisuals.Belt(title.Type, title.IsWorldChampionship, "cp-vacant-belt", title.BeltDesign, title.Name));
                var link = CreateLink(title, navigate); link.AddToClassList("cp-card-title"); card.Add(link);
                var current = new VisualElement(); current.AddToClassList("cp-card-champion"); card.Add(current);
                if (title.Type == ChampionshipType.TagTeam && !string.IsNullOrEmpty(title.HolderId))
                {
                    var teamName = save.TagTeams.FirstOrDefault(t => t.Id == title.HolderId)?.Name ?? "태그팀";
                    var team = new Label(teamName) { tooltip = teamName }; team.AddToClassList("cp-team-name"); current.Add(team);
                    var names = new VisualElement(); names.AddToClassList("cp-card-members"); current.Add(names);
                    foreach (var wrestler in members.Select(id => save.Wrestlers.FirstOrDefault(w => w.Id == id)).Where(w => w != null))
                    { var name = WrestlerProfileController.CreateLink(wrestler); name.tooltip = WrestlerNameText.DisplayName(wrestler); names.Add(name); }
                }
                else Holder(current, save, title, title.HolderId, members);
                var reign = title.Reigns.LastOrDefault(r => !r.LostDate.HasValue);
                var date = new Label(DivisionText(title.Division) + (reign == null ? "" : $" · {Date(reign.WonDate)}")) { name = "championship-division-" + title.Id }; date.AddToClassList("cp-card-date"); card.Add(date);
                if (reign != null) { var duration = new Label($"재위 {Math.Max(0, reign.WonDate.DaysUntil(save.CurrentDate))}일") { name = "championship-reign-days-" + title.Id }; duration.AddToClassList("cp-card-duration"); card.Add(duration); }
                var show = SourceShow(save, title, reign);
                if (show != null) { var source = new Label(show.Name); source.AddToClassList("cp-card-source"); card.Add(source); }
                OperationsView.Selectable(card, () => navigate(title.Id));
            }
            }
        }

        internal static void Overview(VisualElement parent, GameSave save, ChampionshipState title)
        {
            var columns = new VisualElement(); columns.AddToClassList("cp-overview"); parent.Add(columns);
            var media = new VisualElement(); media.AddToClassList("cp-media"); columns.Add(media);
            media.Add(ManagementVisuals.Belt(title.Type, title.IsWorldChampionship, "cp-profile-belt", title.BeltDesign, title.Name));
            var lineup = new VisualElement(); lineup.AddToClassList("cp-profile-champion"); media.Add(lineup);
            var members = ChampionshipService.CurrentMemberIds(save, title);
            lineup.Add(new GroupPortraitView(members.Select(id => save.Wrestlers.FirstOrDefault(w => w.Id == id)), title.Id));
            var championNames = new VisualElement(); championNames.AddToClassList("cp-profile-champion-names"); lineup.Add(championNames); Holder(championNames, save, title, title.HolderId, members);
            var facts = new VisualElement(); facts.AddToClassList("cp-facts"); columns.Add(facts);
            TextRow(facts, "종류", title.Type == ChampionshipType.TagTeam ? "태그팀 챔피언십" : title.IsWorldChampionship ? "월드 챔피언십" : "개인 챔피언십");
            TextRow(facts, "디비전", DivisionText(title.Division));
            Holder(Row(facts, "현재 챔피언"), save, title, title.HolderId, members);
            var reign = title.Reigns.LastOrDefault(r => !r.LostDate.HasValue);
            TextRow(facts, "챔피언 등극일", reign == null ? "기록 없음" : Date(reign.WonDate));
            var show = SourceShow(save, title, reign); TextRow(facts, "등극한 쇼", show?.Name ?? "기록 없음");
            var previous = title.Reigns.LastOrDefault(r => r.LostDate.HasValue);
            var previousRow = Row(facts, "이전 챔피언");
            if (previous == null) previousRow.Add(new Label("기록 없음")); else Holder(previousRow, save, title, previous.HolderId, previous.MemberIds);
            TextRow(facts, "단체", save.Promotion.Name);
            TextRow(facts, "창설일", title.CreatedDate.HasValue ? Date(title.CreatedDate.Value) : "기록 없음");
            TextRow(facts, "위상", $"{ChampionshipService.Prestige(title):0.0} / 100");
            if (reign != null) { TextRow(facts, "현재 재위", $"{Math.Max(0, reign.WonDate.DaysUntil(save.CurrentDate))}일"); TextRow(facts, "방어", $"{reign.Defenses}회"); }
            var records = new VisualElement { name = "championship-records" }; records.AddToClassList("cp-records"); facts.Add(records);
            var recordsTitle = new Label("주요 기록"); recordsTitle.AddToClassList("cp-records-title"); records.Add(recordsTitle);
            var summary = ChampionshipRecords.Calculate(save, title);
            var first = Row(records, "초대 챔피언");
            if (summary.First == null) first.Add(new Label("기록 없음")); else Holder(first, save, title, summary.First.HolderId, summary.First.MemberIds);
            var most = Row(records, "최다 획득");
            foreach (var record in summary.MostWins) RecordHolder(most, save, title, record.Reign, $"{record.Count}회");
            EmptyRecord(most);
            var longest = Row(records, "최장 재위");
            foreach (var record in summary.Longest) RecordHolder(longest, save, title, record.Reign, $"{record.Days}일");
            EmptyRecord(longest);
            var shortest = Row(records, "최단 재위(종료)");
            foreach (var record in summary.Shortest) RecordHolder(shortest, save, title, record.Reign, $"{record.Days}일");
            EmptyRecord(shortest);
            Ages(Row(records, "최고령 획득"), save, summary.Oldest); Ages(Row(records, "최연소 획득"), save, summary.Youngest);
        }

        internal static void Reigns(VisualElement parent, GameSave save, ChampionshipState title)
        {
            var header = new VisualElement(); header.AddToClassList("cp-reign-row"); header.AddToClassList("cp-reign-heading"); parent.Add(header);
            foreach (var text in new[] { "대수", "챔피언", "재위 기간", "재위", "방어" }) Cell(header, text, text == "챔피언" ? "cp-reign-holder" : text == "재위 기간" ? "cp-reign-period" : text == "방어" ? "cp-reign-defenses" : null);
            for (var i = 0; i < title.Reigns.Count; i++)
            {
                var reign = title.Reigns[i]; var row = new VisualElement { name = "championship-reign-" + i }; row.AddToClassList("cp-reign-row"); parent.Add(row);
                Cell(row, (i + 1) + "대"); var holder = new VisualElement(); holder.AddToClassList("cp-reign-holder"); row.Add(holder); Holder(holder, save, title, reign.HolderId, reign.MemberIds);
                var period = new VisualElement(); period.AddToClassList("cp-reign-period"); row.Add(period); period.Add(new Label($"{Date(reign.WonDate)} ~ {(reign.LostDate.HasValue ? Date(reign.LostDate.Value) : "현재")}"));
                var show = SourceShow(save, title, reign); if (show != null) { var source = new Label(show.Name); source.AddToClassList("mg-caption"); period.Add(source); }
                Cell(row, $"{Math.Max(0, reign.WonDate.DaysUntil(reign.LostDate.HasValue ? reign.LostDate.Value : save.CurrentDate))}일"); Cell(row, $"방어 {reign.Defenses}회", "cp-reign-defenses");
            }
            if (title.Reigns.Count == 0) parent.Add(new Label("아직 재위 기록이 없습니다."));
        }

        private static void RecordHolder(VisualElement parent, GameSave save, ChampionshipState title, ChampionshipReignState reign, string value)
        {
            var line = new VisualElement(); line.AddToClassList("cp-record-line"); parent.Add(line); Holder(line, save, title, reign.HolderId, reign.MemberIds); line.Add(new Label(value));
        }
        private static void Ages(VisualElement parent, GameSave save, IEnumerable<ChampionshipAgeRecord> records)
        {
            foreach (var record in records)
            {
                var wrestler = save.Wrestlers.First(w => w.Id == record.WrestlerId); var line = new VisualElement(); line.AddToClassList("cp-record-line"); parent.Add(line);
                line.Add(WrestlerProfileController.CreateLink(wrestler)); line.Add(new Label($"{wrestler.Identity.BirthDate.AgeOn(record.Reign.WonDate)}세"));
            }
            EmptyRecord(parent);
        }
        private static void EmptyRecord(VisualElement row) { if (row.childCount == 0) row.Add(new Label("기록 없음")); }
        private static void Cell(VisualElement row, string text, string className = null)
        {
            var cell = new Label(text); cell.AddToClassList(className ?? "cp-reign-number"); row.Add(cell);
        }
        private static VisualElement Row(VisualElement parent, string key)
        {
            var row = new VisualElement(); row.AddToClassList("cp-info-row"); parent.Add(row); var label = new Label(key); label.AddToClassList("cp-info-key"); row.Add(label);
            var value = new VisualElement(); value.AddToClassList("cp-info-value"); row.Add(value); return value;
        }
        private static void TextRow(VisualElement parent, string key, string value) => Row(parent, key).Add(new Label(value));
        internal static string Date(GameDate date) => ChampionshipRecords.KnownDate(date) ? $"{date.Year}.{date.Month:00}.{date.Day:00}" : "기록 없음";
        private static ShowState SourceShow(GameSave save, ChampionshipState title, ChampionshipReignState reign)
        {
            if (string.IsNullOrEmpty(reign?.SourceResultId)) return null;
            var match = title.MatchHistory.FirstOrDefault(m => m.SourceResultId == reign.SourceResultId);
            return match == null ? null : save.Shows.FirstOrDefault(s => s.Id == match.ShowId);
        }
        private static void Holder(VisualElement parent, GameSave save, ChampionshipState title, string id, IEnumerable<string> members)
        {
            if (string.IsNullOrEmpty(id)) { parent.Add(new Label("공석")); return; }
            if (title.Type == ChampionshipType.TagTeam) { var name = new Label(save.TagTeams.FirstOrDefault(t => t.Id == id)?.Name ?? "태그팀"); name.AddToClassList("cp-team-name"); parent.Add(name); }
            foreach (var wrestler in save.Wrestlers.Where(w => members.Contains(w.Id))) parent.Add(WrestlerProfileController.CreateLink(wrestler));
        }
    }
}
