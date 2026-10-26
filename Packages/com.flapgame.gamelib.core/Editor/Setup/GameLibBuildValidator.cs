using System.Linq;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FlapGame.GameLib.Editor
{
    /// <summary>Fails the player build when GameLib Setup reports errors (missing SDKs, keys, files). Warnings are only logged.</summary>
    internal sealed class GameLibBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var issues = GameLibProject.Diagnose();
            foreach (GameLibIssue warning in issues.Where(i => i.Severity == GameLibIssueSeverity.Warning))
                Debug.LogWarning($"[GameLib] {warning.Message}");

            var errors = issues.Where(i => i.Severity == GameLibIssueSeverity.Error).ToList();
            if (errors.Count == 0)
                return;

            var message = new StringBuilder("[GameLib] Build stopped - fix these in GameLib > Setup:");
            foreach (GameLibIssue error in errors)
                message.Append("\n- ").Append(error.Message);
            throw new BuildFailedException(message.ToString());
        }
    }
}
