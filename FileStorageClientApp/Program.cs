using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FileStorageClientApp
{
    class Program
    {
        private static HttpClient client = new HttpClient();
        private static IConfiguration Configuration;

        static async Task Main(string[] args)
        {
            // Load configuration from appsettings.json
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();

            // Read API URL and Bearer Token from configuration
            //var apiBaseUrl = Configuration["ApiSettings:BaseUrl"];
            var token = await RequestAccessToken();

            // Set Base Address for HttpClient
            //client.BaseAddress = new Uri(apiBaseUrl);

            // Add Bearer Token to HttpClient Authorization Header
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            bool continueOperations = true;

            while (continueOperations)
            {
                Console.WriteLine("Do you want to upload or download a file or list files? (Enter '1' or '2' or '3')");
                var action = Console.ReadLine().Trim().ToLower();

                switch (action)
                {
                    case "1":
                        await UploadFile();
                        break;

                    case "2":
                        await DownloadFile();
                        break;

                    case "3":
                        await GetFilesWithPresignedUrls();
                        break;

                    default:
                        Console.WriteLine("Invalid option, please enter '1' or '2' or '3'.");
                        break;
                }

                // Ask if the user wants to perform another operation
                Console.WriteLine("Do you want to make another request? (y/n)");
                var response = Console.ReadLine().Trim().ToLower();
                if (response != "y")
                {
                    continueOperations = false;
                    Console.WriteLine("Exiting the application...");
                }
            }
        }

        // Method to upload a file to the API
        private static async Task UploadFile()
        {
            Console.Write("Enter the bucket name: ");
            var bucketName = Console.ReadLine().Trim();

            Console.Write("Enter the file path: ");
            var filePath = Console.ReadLine().Trim();

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File does not exist. Please check the path.");
                return;
            }

            var fileName = Path.GetFileName(filePath);

            try
            {
                var form = new MultipartFormDataContent();
                var fileContent = new StreamContent(File.OpenRead(filePath));
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(fileContent, "file", fileName);

                // Call the API to upload the file
                var response = await client.PostAsync($"{Configuration["ApiSettings:BaseUrl"]}/file/upload?bucketName={bucketName}", form);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"File '{fileName}' uploaded successfully to bucket '{bucketName}'.");
                }
                else
                {
                    Console.WriteLine($"Failed to upload file. Status Code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during upload: {ex.Message}");
            }
        }

        // Method to download a file from the API
        private static async Task DownloadFile()
        {
            Console.Write("Enter the bucket name: ");
            var bucketName = Console.ReadLine().Trim();

            Console.Write("Enter the file name: ");
            var fileName = Console.ReadLine().Trim();

            Console.Write("Enter the destination path to save the file: ");
            var destinationPath = Console.ReadLine().Trim();

            try
            {
                // Call the API to download the file
                var response = await client.GetAsync($"{Configuration["ApiSettings:BaseUrl"]}/File/download?bucketName={bucketName}&fileName={fileName}");

                if (response.IsSuccessStatusCode)
                {
                    var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fileStream);
                    fileStream.Close();

                    Console.WriteLine($"File '{fileName}' downloaded successfully to '{destinationPath}'.");
                }
                else
                {
                    Console.WriteLine($"Failed to download file. Status Code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during download: {ex.Message}");
            }
        }

        // Method to call the Web API and print the files with presigned URLs
        private static async Task GetFilesWithPresignedUrls()
        {
            // Ask the user for input
            Console.WriteLine("Enter the bucket name (required):");
            var bucketName = Console.ReadLine().Trim();

            Console.WriteLine("Enter the file name or prefix (optional, press Enter to skip):");
            var fileName = Console.ReadLine().Trim();


            if (string.IsNullOrEmpty(bucketName))
            {
                Console.WriteLine("Bucket name is required.");
                return;
            }

            try
            {
                // Build the API request URL
                var url = $"{Configuration["ApiSettings:BaseUrl"]}/file/files?bucketName={bucketName}";

                if (!string.IsNullOrEmpty(fileName))
                {
                    url += $"&fileName={fileName}";
                }

                // Call the Web API
                HttpResponseMessage response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();

                    // Deserialize the JSON response
                    var files = JsonSerializer.Deserialize<List<FileDetails>>(jsonResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Print the files and their URLs
                    Console.WriteLine($"\nFiles in bucket '{bucketName}':\n");
                    foreach (var file in files)
                    {
                        Console.WriteLine($"File Name: {file.FileName}");
                        Console.WriteLine($"Download URL: {file.PresignedUrl}\n");
                    }
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private static async Task<string> RequestAccessToken()
        {
            using (var client = new HttpClient())
            {
                // Prepare the request content
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", Configuration["ApiSettings:ClientId"]),
                    new KeyValuePair<string, string>("client_secret", Configuration["ApiSettings:ClientSecret"]),
                    new KeyValuePair<string, string>("scope", Configuration["ApiSettings:Scope"])
                });

                var response = await client.PostAsync($"{Configuration["ApiSettings:AuthorityServer"]}/{Configuration["ApiSettings:TokenEndPoint"]}", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(responseContent)["access_token"].ToString();
                }
                return null;
            }
        }

    }

    // Helper class to represent file details from the API
    public class FileDetails
    {
        public string FileName { get; set; }
        public string PresignedUrl { get; set; }
    }
}
