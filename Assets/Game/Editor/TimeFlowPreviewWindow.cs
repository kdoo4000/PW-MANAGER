using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    public sealed class TimeFlowPreviewWindow : EditorWindow
    {
        private long initialCash = 5000;
        private long monthlySalary = 1000;
        private int targetYear = 2026;
        private int targetMonth = 7;
        private int targetDay = 1;
        private GameSave save;
        private TimeFlowResult lastResult;
        private Vector2 scroll;

        [MenuItem("PW Manager/Time Flow Preview")]
        public static void Open()
        {
            GetWindow<TimeFlowPreviewWindow>("Time Flow Preview");
        }

        private void OnEnable()
        {
            minSize = new Vector2(620, 480);
            ResetSimulation();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Time Flow Test Settings", EditorStyles.boldLabel);
            initialCash = EditorGUILayout.LongField("Initial Cash", initialCash);
            monthlySalary = EditorGUILayout.LongField("Monthly Salary", monthlySalary);
            using (new EditorGUILayout.HorizontalScope())
            {
                targetYear = EditorGUILayout.IntField("Target", targetYear);
                targetMonth = EditorGUILayout.IntField(targetMonth, GUILayout.Width(55));
                targetDay = EditorGUILayout.IntField(targetDay, GUILayout.Width(55));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset to 2026-06-01")) ResetSimulation();
                if (GUILayout.Button("Advance 1 Day")) Advance(save.CurrentDate.AddDays(1));
                if (GUILayout.Button("Advance to Target")) AdvanceTarget();
            }

            EditorGUILayout.Space(8);
            DrawSummary();
            DrawHistory();
        }

        private void ResetSimulation()
        {
            var promotionId = Guid.NewGuid().ToString("D");
            var wrestlerId = Guid.NewGuid().ToString("D");
            save = new GameSave
            {
                CurrentDate = new GameDate(2026, 6, 1),
                Promotion = new PromotionState { Id = promotionId, Name = "Preview Promotion", InitialCash = initialCash }
            };
            save.Contracts.Add(new ContractState
            {
                Id = Guid.NewGuid().ToString("D"), PersonId = wrestlerId, Type = ContractType.Wrestler,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31),
                MonthlySalary = monthlySalary, Status = ContractStatus.Active
            });
            lastResult = null;
            Repaint();
        }

        private void AdvanceTarget()
        {
            try { Advance(new GameDate(targetYear, targetMonth, targetDay)); }
            catch (Exception exception) { ShowFailure(exception); }
        }

        private void Advance(GameDate target)
        {
            try
            {
                lastResult = new TimeFlowService().AdvanceTo(save, target);
                if (lastResult.State == TimeFlowState.Blocked)
                    ShowNotification(new GUIContent("Time flow blocked."));
            }
            catch (Exception exception) { ShowFailure(exception); }
            Repaint();
        }

        private void ShowFailure(Exception exception)
        {
            Debug.LogException(exception);
            ShowNotification(new GUIContent(exception.Message));
        }

        private void DrawSummary()
        {
            EditorGUILayout.LabelField("Current State", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Date", Format(save.CurrentDate));
            EditorGUILayout.LabelField("Cash", save.Promotion.CalculateCurrentCash(save.Transactions).ToString("N0"));
            EditorGUILayout.LabelField("Contract", save.Contracts[0].Status.ToString());
            EditorGUILayout.LabelField("Salary Transactions", save.Transactions.Count(x => x.Type == TransactionType.Salary).ToString());
            EditorGUILayout.LabelField("Processed IDs", save.ProcessedIds.Count.ToString());
            if (lastResult != null)
            {
                var message = lastResult.State == TimeFlowState.Blocked
                    ? lastResult.BlockingReason
                    : $"Completed: {lastResult.ProcessedDays} day(s)";
                EditorGUILayout.HelpBox(message, lastResult.State == TimeFlowState.Blocked ? MessageType.Warning : MessageType.Info);
            }
        }

        private void DrawHistory()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Transaction / Processing History", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var transaction in save.Transactions)
                EditorGUILayout.LabelField($"{Format(transaction.Date)}  {transaction.Type}  {transaction.Amount:N0}  Contract {Short(transaction.ReasonId)}");
            foreach (var id in save.ProcessedIds)
                EditorGUILayout.LabelField(id, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private static string Format(GameDate date) => $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
        private static string Short(string id) => string.IsNullOrEmpty(id) || id.Length <= 8 ? id : id.Substring(0, 8);
    }
}
