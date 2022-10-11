namespace Kyber.Core;

public interface ISystemCollection
{
    Type[] StartupSystems { get; }
    Type[] UpdateSystems { get; }
    Type[] RenderSystems { get; }
}
