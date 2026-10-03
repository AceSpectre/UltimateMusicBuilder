using Microsoft.Extensions.Options;
using Moq;
using Sma5h;
using Sma5h.Data;
using Sma5h.ResourceProviders;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class MsbtResourceProviderTests
    {
        [Fact]
        public void WriteFile_ReturnsFalse_WhenGenerationFails()
        {
            var provider = new MsbtResourceProvider(new Mock<IOptionsMonitor<Sma5hOptions>>().Object,
                TestEnvironment.CreateLogger<MsbtResourceProvider>());
            var missingInput = Path.Combine(Path.GetTempPath(), "umb-missing-" + Guid.NewGuid().ToString("N") + ".msbt");
            var output = Path.Combine(Path.GetTempPath(), "umb-out-" + Guid.NewGuid().ToString("N") + ".msbt");

            Assert.False(provider.WriteFile(missingInput, output, new MsbtDatabase()));
        }
    }
}
