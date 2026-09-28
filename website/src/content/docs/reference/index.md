---
title: Reference
description: Lookup tables for Ion, covering stage orders, diagnostics, configuration keys, packages and the changelog.
sidebar:
  order: 0
  label: Overview
---

The reference section is for looking things up rather than learning them. Each page is a set of tables built from the
source code: the constants, descriptors, options classes and project files they document.

| Page | Use it to |
|---|---|
| [Stage order](/Ion/reference/stage-order/) | Find every `StageOrder` constant, its value and which engine system uses it, and see each stage's run order. |
| [Diagnostics](/Ion/reference/diagnostics/) | Look up an `ION` id from a build error or an `IonScheduleException`: severity, message, cause and fix. |
| [Configuration keys](/Ion/reference/configuration/) | Find every `Ion:*` key with its type and default, grouped by module, and the short command line switches. |
| [Module map](/Ion/reference/module-map/) | Pick packages: what each one is for, its `AddX`/`UseX` entry points and what it pulls in. |
| [Changelog](/Ion/reference/changelog/) | See what changed between versions and how to upgrade from 0.2. |

## Quick answers

**Which order should my step use?** Leave it at the default (0) unless it must run relative to an engine step. Your
steps always run between engine setup (-1000 to -500) and teardown (500 to 1000). See
[Stage order](/Ion/reference/stage-order/#choosing-an-order-for-your-own-steps).

**What does ION0xx mean?** `ION001` to `ION014` are schedule problems, `ION1xx` events, `ION2xx` networking, `ION3xx`
ECS queries and `ION4xx` web routes. See [Diagnostics](/Ion/reference/diagnostics/).

**How do I set a key on the command line?** `--Ion:Section:Key=value`, for example
`dotnet run -- --Ion:Graphics:PreferredBackend=OpenGLES`. See [Configuration keys](/Ion/reference/configuration/).

**Which package has `AddPhysics2D`?** `Ion.Extensions.Physics2D`. See the [Module map](/Ion/reference/module-map/).

## Printing the live values

The reference pages describe the engine; your game's actual state is one flag away:

```bash
dotnet run --project MyGame -- --Ion:PrintSchedule=true    # every stage's steps with their orders
ion schedule MyGame                                        # the same, from the ion tool
ion run MyGame --headless --frames 60 --summary run.json   # frame stats, counters, warnings and the schedule as JSON
```

```csharp
using var game = builder.Build();
game.UseIon().UseSystem<GameSystem>();
Console.WriteLine(game.PrintSchedule());
```

## Other references

- The API itself is documented in XML comments on every public type; your IDE shows them.
- The design documents in [`docs/design`](https://github.com/jimbuck/Ion/tree/main/docs/design) explain the rendering,
  physics, networking, UI, web and remote modules in depth.
- The platform notes in [`docs/platforms`](https://github.com/jimbuck/Ion/tree/main/docs/platforms) are the source of
  the [Platforms](/Ion/platforms/overview/) section.
- The [examples](/Ion/examples/) are complete, tested programs for every major feature.
