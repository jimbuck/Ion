using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Ion;

public interface IIonApplicationBuilder
{
	ConfigurationManager Configuration { get; }
	IServiceCollection Services { get; }
}
