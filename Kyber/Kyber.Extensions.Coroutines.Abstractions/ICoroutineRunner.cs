using System.Collections;

namespace Kyber
{
	public interface ICoroutineRunner
	{
		void Start(IEnumerator routine);
		void Stop(IEnumerator routine);
		void StopAll();
		bool IsActive(IEnumerator routine);
		
		void Update(GameTime dt);
	}
}