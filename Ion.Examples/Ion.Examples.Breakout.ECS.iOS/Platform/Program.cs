using Silk.NET.Windowing.Sdl.iOS;

using Ion.Examples.Breakout.ECS;

// SDL owns the UIKit main loop on iOS: SilkMobile.RunApp starts it and calls the game on SDL's main thread. The assets are
// bundle resources, so the content root is the app bundle (AppContext.BaseDirectory).
SilkMobile.RunApp(args, static a => BreakoutMobile.Run(AppContext.BaseDirectory, a));
