namespace Ion.Tests
{
    public class BuilderTests
    {
        [Fact, Trait(CATEGORY, UNIT)]
        public void CreateDefaultBuilder()
        {
            var builder = IonApplication.CreateBuilder();
            Assert.NotNull(builder);
            var host = builder.Build();
            Assert.NotNull(host);
        }
    }
}