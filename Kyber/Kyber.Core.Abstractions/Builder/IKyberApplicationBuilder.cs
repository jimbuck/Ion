using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kyber.Builder;

public interface IKyberApplicationBuilder
{
	ConfigurationManager Configuration { get; }
	IServiceCollection Services { get; }
}
