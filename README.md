# Ion Engine
A small, positively-charged, middleware-based game engine for C#.

- **Modern:** Built using modern C# features and design patterns, Ion setup closely resembles ASP.NET Core setup.
- **Modular:** Ion is a collection of modules that build on the `Ion.Core` module. You can use as many or as few modules as you want.
- **Middleware:** Using middleware, you can easily add functionality to the engine without having to modify the engine itself. This allows for easy extensibility and customization.

----

## Modules

### Ion.Core
A simple, modern code-first game engine inspired by Monogame and Bevy with an API like ASP.NET Core. This core module contains the core engine and all of the core features:
  - Application builder
  - Game loop
  - Events
  - Storage

### Ion.Extensions.Scenes
Adds support for scenes that each have thier own scope for dependency injection!

### Ion.Extensions.Coroutines
Adds support for coroutines, allowing for async code to be run in a synchronous manner.

### Ion.Extensions.Debug
Adds support for debug utils such as a trace profiler and debug renderer.

### Ion.Extensions.Graphics.Veldrid
Adds window, input, and graphics support using the Veldrid API. Includes a built-in sprite batch for easy 2D rendering.

----

## Planned Modules
 - **Ion.Extensions.UI**
 - **Ion.Extensions.Networking**
 - **Ion.Extensions.Physics**
 - Multi-platform support

## Built Using
  - [Veldrid](https://github.com/veldrid/veldrid) for Graphics
  - [Assimp.Net](https://github.com/StirlingLabs/Assimp.Net) for asset loading
  - [Peridot by Ezequias Silva](https://github.com/ezequias2d/peridot) for Sprite Batch
  - [Coroutines by ChevyRay](https://github.com/ChevyRay/Coroutines)

## Contributing

WIP

### Useful Links

- [Graph Visualizer](https://csacademy.com/app/graph_editor/): To visual archetype graph.

----

## License
Ion Engine is [MIT licensed](./LICENSE).
