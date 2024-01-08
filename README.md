# Kyber
A modern, modular, middleware-based game engine for C#.

- **Modern:** Built using modern C# features and design patterns, Kyber setup closely resembles ASP.NET Core setup.
- **Modular:** Kyber is a collection of modules that build on the `Kyber.Core` module. You can use as many or as few modules as you want.
- **Middleware:** Using middleware, you can easily add functionality to the engine without having to modify the engine itself. This allows for easy extensibility and customization.

----

## Modules

### Kyber.Core
A simple, modern code-first game engine inspired by Monogame and Bevy with an API like ASP.NET Core. This core module contains the core engine and all of the core features:
  - Application builder
  - Game loop
  - Events
  - Storage

### Kyber.Extensions.Scenes
Adds support for scenes that each have thier own scope for dependency injection!

### Kyber.Extensions.Coroutines
Adds support for coroutines, allowing for async code to be run in a synchronous manner.

### Kyber.Extensions.Debug
Adds support for debug utils such as a trace profiler and debug renderer.

### Kyber.Extensions.Graphics.Veldrid
Adds window, input, and graphics support using the Veldrid API. Includes a built-in sprite batch for easy 2D rendering.

----

## Planned Modules
 - **Kyber.Extensions.UI**
 - **Kyber.Extensions.Networking**
 - **Kyber.Extensions.Physics**
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
Kyber is [MIT licensed](./LICENSE).
