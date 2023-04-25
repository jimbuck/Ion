
using Kyber.Hosting.Scenes;

namespace Kyber.Examples.Scenes;

public static class Scenes
{
    public class Main : ISceneConfiguration
    {
        public void Configure(ISceneBuilder scene)
        {
            Console.WriteLine($"Configuring Main scene...");
            scene.AddSystem<TestLoggerSystem>();
        }
    }

    public static void Gameplay(ISceneBuilder scene)
    {
        Console.WriteLine($"Configuring Gameplay scene...");
        scene.AddSystem<TestLoggerSystem>();
    }
}