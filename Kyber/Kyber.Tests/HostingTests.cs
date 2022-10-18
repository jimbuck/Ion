namespace Kyber.Hosting.Tests
{
    public class HostingTests
    {
        [Fact]
        public void CreateDefaultBuilder()
        {
            var builder = KyberHost.CreateDefaultBuilder();
            Assert.NotNull(builder);
            var host = builder.Build();
            Assert.NotNull(host);
        }
    }
}