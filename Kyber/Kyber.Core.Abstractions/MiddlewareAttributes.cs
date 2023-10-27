
namespace Kyber;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class InitAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class FirstAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class UpdateAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class FixedUpdateAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class RenderAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class LastAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] public class DestroyAttribute : Attribute { }
