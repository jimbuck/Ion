using System.Collections;

namespace Ion
{
	public interface ICoroutineRunner
	{
		/// <summary>
		/// Number of currently active coroutines.
		/// </summary>
		int Count { get; }

		/// <summary>
		/// Run a coroutine.
		/// </summary>
		/// <param name="routine">
		/// The routine to run. It may yield <c>null</c>, a <see cref="float"/> number of seconds, a <see cref="Wait"/>, an
		/// <see cref="IWait"/> or a nested <see cref="IEnumerator"/>. An <see cref="IEnumerator{T}"/> of <see cref="Wait"/> is
		/// read without boxing, so it allocates nothing per yield.
		/// </param>
		void Start(IEnumerator routine);

		/// <summary>
		/// Stop the specified routine.
		/// </summary>
		/// <param name="routine">The routine to stop.</param>
		void Stop(IEnumerator routine);

		/// <summary>
		/// Stop all running routines.
		/// </summary>
		void StopAll();

		/// <summary>
		/// Check if the routine is currently active.
		/// </summary>		
		/// <param name="routine">The routine to check.</param>
		/// <returns>True if the routine is active.</returns>
		bool IsActive(IEnumerator routine);

		/// <summary>
		/// Update all running coroutines.
		/// </summary>
		/// <param name="dt">GameTime of the current frame.</param>
		void Update(GameTime dt);
	}
}