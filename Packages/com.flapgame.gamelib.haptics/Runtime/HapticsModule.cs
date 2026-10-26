using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Haptics
{
    /// <summary>Registers <see cref="IHapticsService"/>. Created by GameLib > Setup.</summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Haptics", fileName = "HapticsModule")]
    public sealed class HapticsModule : GameLibModule
    {
        [SerializeField] private HapticSettings settings = new();

        public HapticSettings Settings => settings;

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            services.Register<IHapticsService>(new HapticsService(settings));
            return default;
        }
    }
}
