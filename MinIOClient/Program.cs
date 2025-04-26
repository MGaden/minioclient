using Minio;
using Minio.DataModel;
using Minio.Exceptions;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using Minio.DataModel.Args;

class Program
{
    static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory()) // Ensure it reads from the current directory
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Read MinIO configuration from appsettings.json
        var endpoint = config["MinIOConfig:Endpoint"];
        var accessKey = config["MinIOConfig:AccessKey"];
        var secretKey = config["MinIOConfig:SecretKey"];
        var bucketName = config["MinIOConfig:BucketName"];

        // Create a MinIO client instance
        var minioClient = new MinioClient()
                             .WithEndpoint(endpoint)
                             .WithCredentials(accessKey, secretKey)
                             .WithSSL(false) // Set to true if using SSL
                             .Build();

        try
        {
            // Create the bucket if it doesn't exist using the new BucketExistsArgs
            bool found = await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
            if (!found)
            {
                await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
                Console.WriteLine($"Bucket {bucketName} created successfully.");
            }

            // Ask user whether to upload or retrieve a file
            Console.WriteLine("Do you want to (1) Upload a file or (2) Retrieve a file? Enter 1 or 2:");
            var choice = Console.ReadLine();

            if (choice == "1")
            {
                // Upload a file
                await UploadFile(minioClient, bucketName);
            }
            else if (choice == "2")
            {
                // Retrieve a file
                await RetrieveFile(minioClient, bucketName);
            }
            else
            {
                Console.WriteLine("Invalid choice.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.ReadLine();
    }

    // Method to upload a file to MinIO
    private static async Task UploadFile(IMinioClient minioClient, string bucketName)
    {
        // Ask user for the full file path and name
        Console.WriteLine("Please enter the full path of the file you want to upload:");
        var filePath = Console.ReadLine();

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        // Extract the file name from the full path
        var fileName = Path.GetFileName(filePath);

        // Upload the file (PutObject)
        Console.WriteLine($"Uploading {fileName} to bucket {bucketName}...");
        await minioClient.PutObjectAsync(new PutObjectArgs()
                                             .WithBucket(bucketName)
                                             .WithObject(fileName)
                                             .WithFileName(filePath)
                                             .WithContentType("application/octet-stream")); // Using a generic content type for any file

        Console.WriteLine($"Successfully uploaded {fileName}.");
    }

    // Method to retrieve a file from MinIO
    private static async Task RetrieveFile(IMinioClient minioClient, string bucketName)
    {
        // Ask user for the object name and the path where to save it
        Console.WriteLine("Please enter the file name you want to retrieve from the bucket:");
        var objectName = Console.ReadLine();

        Console.WriteLine("Please enter the full path where you want to save the file:");
        var downloadPath = Console.ReadLine();

        // Ensure the download directory exists
        var downloadDirectory = Path.GetDirectoryName(downloadPath);
        if (!Directory.Exists(downloadDirectory))
        {
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(downloadDirectory);
            Console.WriteLine($"Created directory: {downloadDirectory}");
        }

        // Retrieve the file (GetObject)
        Console.WriteLine($"Retrieving {objectName} from bucket {bucketName}...");

        await minioClient.GetObjectAsync(new GetObjectArgs()
                                             .WithBucket(bucketName)
                                             .WithObject(objectName)
                                             .WithCallbackStream(async (stream) =>
                                             {
                                                 using (var fileStream = File.Create(downloadPath))
                                                 {
                                                     await stream.CopyToAsync(fileStream);
                                                 }
                                                 Console.WriteLine($"Successfully downloaded {objectName} to {downloadPath}.");
                                             }));
    }
}
