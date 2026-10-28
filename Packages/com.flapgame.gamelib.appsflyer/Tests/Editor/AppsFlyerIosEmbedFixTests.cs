using NUnit.Framework;
using FlapGame.GameLib.AppsFlyer.Editor;

namespace FlapGame.GameLib.AppsFlyer.Tests
{
    public class AppsFlyerIosEmbedFixTests
    {
        [Test]
        public void PodsInPodfile_TheirFrameworksAreUnembedded()
        {
            const string podfile = "target 'UnityFramework' do\n" +
                                   "  pod 'AppsFlyerFramework', '7.0.2'\n" +
                                   "  pod 'AppsFlyerRPC', '7.0.13'\n" +
                                   "  pod 'PurchaseConnector', '7.0.2'\n" +
                                   "end\n";
            Assert.That(AppsFlyerIosEmbedFix.FrameworksProvidedByPods(podfile),
                Is.EquivalentTo(new[] { "AppsFlyerLib.framework", "PurchaseConnector.framework" }));
        }

        [Test]
        public void SwiftPackages_NothingIsUnembedded()
        {
            const string podfile = "target 'UnityFramework' do\n" +
                                   "  pod 'Firebase/Analytics', '12.10.0'\n" +
                                   "end\n";
            Assert.That(AppsFlyerIosEmbedFix.FrameworksProvidedByPods(podfile), Is.Empty);
        }
    }
}
