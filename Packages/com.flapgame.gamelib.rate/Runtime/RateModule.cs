using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Rate
{
    /// <summary>Registers <see cref="IRateService"/>. Created by GameLib > Setup.</summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Rate", fileName = "RateModule")]
    public sealed class RateModule : GameLibModule
    {
        [SerializeField] private RateSettings settings = new();

        public RateSettings Settings => settings;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            services.TryResolve(out IAppLifecycle lifecycle);
            services.Register<IRateService>(new RateService(settings.Clone(), ReviewBackends.ForPlatform(), lifecycle));
            return default;
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            string id = settings.AppStoreId?.Trim();
            if (!string.IsNullOrEmpty(id) && !RateService.IsAppStoreId(id))
                issues.Add(GameLibIssue.Error($"Rate: App Store Id '{id}' must be the digits only (apps.apple.com/app/id<digits>)."));
            if (string.IsNullOrEmpty(id))
                issues.Add(GameLibIssue.Warning("Rate: App Store Id is empty - on iOS there is no store page when the review sheet is not available."));
            if (string.IsNullOrWhiteSpace(settings.AndroidReviewLibrary))
                issues.Add(GameLibIssue.Warning("Rate: Android Review Library is empty - Android opens the Play store page instead of the review sheet."));
        }
    }
}
