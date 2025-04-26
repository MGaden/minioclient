using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;

namespace FileStorageAPI.HealthCheck
{
    public class MinioHealthCheck : IHealthCheck
    {
        private readonly IMinioClient _minioClient;

        public MinioHealthCheck(IMinioClient minioClient)
        {
            _minioClient = minioClient;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Attempt to list buckets to check the MinIO server's health
                await _minioClient.ListBucketsAsync(cancellationToken);
                return HealthCheckResult.Healthy("MinIO is available.");
            }
            catch (TaskCanceledException ex)
            {
                // This occurs when the operation times out
                return HealthCheckResult.Unhealthy("MinIO health check timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                // This occurs when there are network-related issues or if the MinIO server is down
                return HealthCheckResult.Unhealthy("MinIO server is unreachable.", ex);
            }
            catch (Exception ex)
            {
                // Catch any other exceptions and mark MinIO as unhealthy
                return HealthCheckResult.Unhealthy("MinIO encountered an error.", ex);
            }
        }
    }
}
