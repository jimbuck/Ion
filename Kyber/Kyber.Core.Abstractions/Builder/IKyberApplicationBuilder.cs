using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kyber;

public interface IKyberApplicationBuilder
{
	ConfigurationManager Configuration { get; }
	IServiceCollection Services { get; }
}
