using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace XenChat.Services
{
    public class CloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(IConfiguration configuration)
        {
            var cloudName = configuration["Cloudinary:CloudName"]
                ?? Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME")
                ?? throw new InvalidOperationException("Cloudinary CloudName is not configured.");

            var apiKey = configuration["Cloudinary:ApiKey"]
                ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY")
                ?? throw new InvalidOperationException("Cloudinary ApiKey is not configured.");

            var apiSecret = configuration["Cloudinary:ApiSecret"]
                ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
                ?? throw new InvalidOperationException("Cloudinary ApiSecret is not configured.");

            var account = new Account(cloudName, apiKey, apiSecret);
            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
        }

        /// <summary>
        /// Uploads an IFormFile to Cloudinary under the given folder and returns the secure URL.
        /// </summary>
        public async Task<string> UploadImageAsync(IFormFile file, string folder)
        {
            using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false,
                Transformation = new Transformation()
                    .Quality("auto")
                    .FetchFormat("auto")
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new Exception($"Cloudinary upload failed: {result.Error.Message}");

            return result.SecureUrl.ToString();
        }

        /// <summary>
        /// Deletes an image from Cloudinary by its public ID.
        /// </summary>
        public async Task DeleteImageAsync(string publicId)
        {
            var deleteParams = new DeletionParams(publicId);
            await _cloudinary.DestroyAsync(deleteParams);
        }
    }
}
