// Runs the physics replay scenes of the Stage 5b tests and prints their hashes: PhysicsReplay [steps=10000]
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Physics2D.Tests;
using Ion.Extensions.Physics3D.Tests;

var steps = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 10_000;
Console.WriteLine($"# {RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.ProcessArchitecture}, AOT={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}, Vector<float>.Count={Vector<float>.Count}, {steps} steps");
Console.WriteLine($"2D (Box2D v3): {Replay2D.Run(steps):X16}");
Console.WriteLine($"3D (BepuPhysics v2, single-threaded): {Replay3D.Run(steps):X16}");
