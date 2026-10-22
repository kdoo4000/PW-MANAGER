using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PWManager.Editor
{
    public static class MatchViewerFlowAudit
    {
        public static void Run()
        {
            var started = DateTime.UtcNow;
            // executeMethod runs after startup reloads, when the Pipeline evaluator is stable.
            var command = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Pipeline.Runtime.Commands.CodeEvalCommand"))
                .First(t => t != null);
            command.GetMethod("EvaluateFile").Invoke(null, new object[]
                { Path.GetFullPath("artifacts/singles-flow-fix-20261015/run-after.cs"), 240000 });
            const string output = "artifacts/singles-flow-fix-20261015/after-final";
            if (!File.Exists(output + "/summary.json") || !File.Exists(output + "/completed.txt") ||
                File.GetLastWriteTimeUtc(output + "/completed.txt") < started)
                throw new InvalidOperationException("Match audit did not produce its summary.");
            Debug.Log("Singles flow comparison written to " + output);
        }
    }
}
