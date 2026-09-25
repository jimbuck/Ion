using System.Runtime.CompilerServices;

namespace Ion.Benchmarks;

/// <summary>
/// Prototype of a delegate-free middleware chain: each hop is a constrained call on a value type, so the JIT (and NativeAOT's ILC,
/// which has no dynamic PGO and therefore cannot devirtualize delegates) can inline the whole chain.
/// Compared against the closure chain the engine builds today and against direct calls. Fixed depth of 8 middleware systems.
/// Note: both the system slot and the continuation must be value types. Calling a generic method through an interface on a
/// class-constrained type parameter is a generic virtual call (runtime dictionary lookup, ~10 ns per hop); the `StructGenericChain_ClassConstraint`
/// variant is kept to show that trap.
/// </summary>
[MemoryDiagnoser]
public class InliningBenchmarks
{
	public interface IStage { void Run(GameTime dt); }

	public interface IMiddlewareSystem { void Invoke<TNext>(GameTime dt, ref TNext next) where TNext : struct, IStage; }

	public struct Terminal : IStage
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Run(GameTime dt) { }
	}

	public struct Chain<TSystem, TNext> : IStage where TSystem : struct, IMiddlewareSystem where TNext : struct, IStage
	{
		public TSystem System;
		public TNext Next;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Run(GameTime dt) => System.Invoke(dt, ref Next);
	}

	/// <summary>The anti-pattern: a class-constrained slot forces a generic virtual (interface) call per hop.</summary>
	public struct ClassChain<TSystem, TNext> : IStage where TSystem : class, IMiddlewareSystem where TNext : struct, IStage
	{
		public TSystem System;
		public TNext Next;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Run(GameTime dt) => System.Invoke(dt, ref Next);
	}

	/// <summary>Value-type adapter a generator would emit around each registered (class) system instance.</summary>
	public struct CounterSlot : IMiddlewareSystem
	{
		public CounterMiddleware Instance;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Invoke<TNext>(GameTime dt, ref TNext next) where TNext : struct, IStage => Instance.Invoke(dt, ref next);
	}

	public sealed class CounterMiddleware : IMiddlewareSystem
	{
		public int Count;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Invoke<TNext>(GameTime dt, ref TNext next) where TNext : struct, IStage
		{
			Count++;
			next.Run(dt);
		}

		public void Before(GameTime dt, GameLoopDelegate next) { Count++; next(dt); }
	}

	private GameTime _dt = null!;
	private CounterMiddleware _system = null!;
	private GameLoopDelegate _closureChain = null!;
	private Chain<CounterSlot, Chain<CounterSlot, Chain<CounterSlot, Chain<CounterSlot, Chain<CounterSlot, Chain<CounterSlot, Chain<CounterSlot, Chain<CounterSlot, Terminal>>>>>>>> _structChain;
	private ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, ClassChain<CounterMiddleware, Terminal>>>>>>>> _classChain;
	private CounterMiddleware[] _flat = null!;

	[GlobalSetup]
	public void Setup()
	{
		_dt = BenchUtils.NewGameTime();
		_system = new CounterMiddleware();

		GameLoopDelegate chain = static _ => { };
		for (var i = 0; i < 8; i++)
		{
			var next = chain;
			var sys = _system;
			chain = dt => sys.Before(dt, next);
		}
		_closureChain = chain;

		var s = _system;
		var slot = new CounterSlot { Instance = s };
		_structChain = new()
		{
			System = slot, Next = new() { System = slot, Next = new() { System = slot, Next = new() { System = slot, Next = new() { System = slot, Next = new() { System = slot, Next = new() { System = slot, Next = new() { System = slot, Next = default } } } } } } }
		};
		_classChain = new()
		{
			System = s, Next = new() { System = s, Next = new() { System = s, Next = new() { System = s, Next = new() { System = s, Next = new() { System = s, Next = new() { System = s, Next = new() { System = s, Next = default } } } } } } }
		};

		_flat = new CounterMiddleware[8];
		Array.Fill(_flat, _system);
	}

	[Benchmark(Baseline = true)]
	public int DirectCalls_8()
	{
		var systems = _flat;
		for (var i = 0; i < systems.Length; i++) systems[i].Count++;
		return _system.Count;
	}

	[Benchmark]
	public int ClosureChain_8()
	{
		_closureChain(_dt);
		return _system.Count;
	}

	[Benchmark]
	public int StructGenericChain_8()
	{
		_structChain.Run(_dt);
		return _system.Count;
	}

	[Benchmark]
	public int StructGenericChain_ClassConstraint_8()
	{
		_classChain.Run(_dt);
		return _system.Count;
	}
}
