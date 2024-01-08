namespace Kyber.Tests
{
    public class BuilderTests
    {
        [Fact, Trait(CATEGORY, UNIT)]
        public void CreateDefaultBuilder()
        {
            var builder = KyberApplication.CreateBuilder();
            Assert.NotNull(builder);
            var host = builder.Build();
            Assert.NotNull(host);
        }
    }
}