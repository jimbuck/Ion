namespace Kyber.Tests
{
    public class HostingTests
    {
        [Fact, Trait(CATEGORY, UNIT)]
        public void CreateDefaultBuilder()
        {
            var builder = KyberHost.CreateDefaultBuilder();
            Assert.NotNull(builder);
            var host = builder.Build();
            Assert.NotNull(host);
        }
    }
}