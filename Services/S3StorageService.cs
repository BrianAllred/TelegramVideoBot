using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using TelegramVideoBot.Utilities;

namespace TelegramVideoBot.Services;

public class S3StorageService
{
    private readonly AmazonS3Client? client;
    private readonly EnvironmentConfig config;

    public bool IsEnabled => config.S3Enabled;
    public int PresignExpiryDays => config.S3PresignExpiryDays;

    public S3StorageService(EnvironmentConfig config)
    {
        this.config = config;

        if (!config.S3Enabled) return;

        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = config.S3ForcePathStyle,
            AuthenticationRegion = config.S3Region
        };

        if (!string.IsNullOrEmpty(config.S3Endpoint))
        {
            s3Config.ServiceURL = config.S3Endpoint;
        }
        else
        {
            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(config.S3Region);
        }

        client = new AmazonS3Client(config.S3AccessKey, config.S3SecretKey, s3Config);
    }

    public async Task<string> UploadAndGetPresignedUrl(string filePath, string objectKey)
    {
        if (client == null)
            throw new InvalidOperationException("S3 storage is not configured");

        var putRequest = new PutObjectRequest
        {
            BucketName = config.S3Bucket,
            Key = objectKey,
            FilePath = filePath,
            DisablePayloadSigning = config.S3DisablePayloadSigning
        };

        await client.PutObjectAsync(putRequest);

        var presignRequest = new GetPreSignedUrlRequest
        {
            BucketName = config.S3Bucket,
            Key = objectKey,
            Expires = DateTime.UtcNow.AddDays(config.S3PresignExpiryDays),
            Verb = HttpVerb.GET
        };

        return await client.GetPreSignedURLAsync(presignRequest);
    }
}
