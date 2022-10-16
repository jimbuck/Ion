
using Kyber.Core.Scenes.Hosting;

namespace Kyber.Examples.Simple;

public static class Scenes
{
    public class Main : ISceneConfiguration
    {
        public void Configure(ISceneBuilder scene)
        {
            Console.WriteLine($"Configuring Main scene...");
            scene.AddSystem<ComprehnsiveLoggerSystem>();
        }
    }

    public static void Gameplay(ISceneBuilder scene)
    {
        Console.WriteLine($"Configuring Gameplay scene...");
        scene.AddSystem<ComprehnsiveLoggerSystem>();
    }
}