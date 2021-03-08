using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QrCodeGenerator.Mvc
{
    public static class QrCodeDependencyInjectionExtensions
    {
        public static void AddQrCodeTagHelper(this IServiceCollection services, IConfiguration configuration)
        {
            var qrCodeConfiguration = new QrCodeConfiguration(configuration);
            services.AddQrCodeTagHelper(qrCodeConfiguration);
        }

        public static void AddQrCodeTagHelper(this IServiceCollection services, QrCodeConfiguration configuration)
        {
            services.AddSingleton(configuration);
        }
    }
}