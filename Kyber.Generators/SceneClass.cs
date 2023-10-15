namespace Kyber.Generators;

internal record struct BaseClass(string Namespace, string ClassName)
{
	public string FullName => $"{Namespace}.{ClassName}";
}

internal record struct SceneClass(string Namespace, string ClassName, ICollection<SystemClass> Systems, ICollection<LifecycleMethodCall> UpdateCalls, ICollection<LifecycleMethodCall> DrawCalls)
{
	public string FullName = $"{Namespace}.{ClassName}";
}

internal record struct SystemClass(string Namespace, string ClassName, string InstanceName)
{
	public string FullName = $"{Namespace}.{ClassName}";
}

internal record struct LifecycleMethodCall(SystemClass System, string MethodName, int? Order);
