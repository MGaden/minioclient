using FileStorageAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.ApiEndpoints;
using Minio.DataModel;
using Minio.DataModel.Args;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace FileStorageAPI.Controllers
{
    /// <summary>
    /// Handles file upload, download, and file listing operations in the MinIO bucket.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        private readonly IMinioClient _minioClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileController"/> class.
        /// </summary>
        /// <param name="minioClient">MinIO client used for interacting with object storage.</param>
        public FileController(IMinioClient minioClient)
        {
            _minioClient = minioClient;
        }

        /// <summary>
        /// Uploads a file to the specified bucket.
        /// </summary>
        /// <param name="bucketName">The name of the bucket to which the file will be uploaded.</param>
        /// <param name="file">The file to be uploaded.</param>
        /// <returns>HTTP 200 OK with the file and bucket details if successful; otherwise, an error response.</returns>
        /// <response code="200">File uploaded successfully.</response>
        /// <response code="400">File not provided or invalid input.</response>
        /// <response code="500">An internal server error occurred during upload.</response>
        [HttpPost("upload")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadFile([FromQuery] string bucketName, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File not provided.");

            var fileName = file.FileName;

            using (var stream = file.OpenReadStream())
            {
                await _minioClient.PutObjectAsync(new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(fileName)
                    .WithStreamData(stream)
                    .WithObjectSize(file.Length)
                    .WithContentType(file.ContentType));
            }

            return Ok(new { fileName, bucketName });
        }

        /// <summary>
        /// Downloads a file from the specified bucket.
        /// </summary>
        /// <param name="bucketName">The name of the bucket containing the file.</param>
        /// <param name="fileName">The name of the file to be downloaded.</param>
        /// <returns>The file stream if found, or an error response.</returns>
        /// <response code="200">File downloaded successfully.</response>
        /// <response code="404">File or bucket not found.</response>
        /// <response code="500">An internal server error occurred during download.</response>
        [HttpGet("download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DownloadFile([FromQuery] string bucketName, [FromQuery] string fileName)
        {
            var memoryStream = new MemoryStream();

            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName)
                .WithCallbackStream(async (stream) =>
                {
                    await stream.CopyToAsync(memoryStream);
                }));

            memoryStream.Position = 0;
            return File(memoryStream, "application/octet-stream", fileName);
        }

        /// <summary>
        /// Lists all files in the specified bucket and returns presigned URLs for each file.
        /// </summary>
        /// <param name="bucketName">The name of the bucket to list files from.</param>
        /// <param name="fileName">Optional: The name or prefix of the file to filter results.</param>
        /// <returns>A list of files and their presigned URLs.</returns>
        /// <response code="200">Files listed successfully.</response>
        /// <response code="400">Invalid input or missing bucket name.</response>
        /// <response code="404">Bucket not found or no files in the bucket.</response>
        /// <response code="500">An internal server error occurred while listing files.</response>
        [HttpGet("files")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetFilesWithPresignedUrls([FromQuery] string bucketName, [FromQuery] string fileName = null)
        {
            if (string.IsNullOrEmpty(bucketName))
            {
                return BadRequest("Bucket name is required.");
            }

            try
            {
                bool bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
                if (!bucketExists)
                {
                    return NotFound($"Bucket '{bucketName}' does not exist.");
                }

                var result = await ListFilesWithPresignedUrls(bucketName, fileName);

                if (result.Count == 0)
                {
                    return NotFound("No files found.");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to list files and generate presigned URLs for each.
        /// </summary>
        /// <param name="bucketName">The name of the bucket.</param>
        /// <param name="fileName">Optional: Prefix or full name to filter files.</param>
        /// <returns>A list of file details, including file names and presigned URLs.</returns>
        private async Task<List<FileDetailsDto>> ListFilesWithPresignedUrls(string bucketName, string fileName)
        {
            var fileDetailsList = new List<FileDetailsDto>();

            try
            {
                IObservable<Item> observable = _minioClient.ListObjectsAsync(
                    new ListObjectsArgs()
                        .WithBucket(bucketName)
                        .WithPrefix(fileName)
                        .WithRecursive(true)
                );

                await observable.ForEachAsync(async item =>
                {
                    if (!item.IsDir)
                    {
                        string presignedUrl = await GeneratePresignedUrl(bucketName, item.Key);
                        fileDetailsList.Add(new FileDetailsDto
                        {
                            FileName = item.Key,
                            PresignedUrl = presignedUrl
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"Error listing files: {ex.Message}");
            }

            return fileDetailsList;
        }

        /// <summary>
        /// Helper method to generate a presigned URL for a specific file.
        /// </summary>
        /// <param name="bucketName">The name of the bucket.</param>
        /// <param name="objectName">The name of the file (object) in the bucket.</param>
        /// <returns>A presigned URL for the specified file.</returns>
        private async Task<string> GeneratePresignedUrl(string bucketName, string objectName)
        {
            try
            {
                var expiry = 60 * 60;  // 1 hour in seconds

                var presignedUrl = await _minioClient.PresignedGetObjectAsync(
                    new PresignedGetObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectName)
                        .WithExpiry(expiry)
                );

                return presignedUrl;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating presigned URL for {objectName}: {ex.Message}");
            }
        }
    }
}
