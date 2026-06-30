using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using nhahangchay.Models;
using System.Threading.Tasks;

namespace nhahangchay.Services
{
    public class CloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService(IOptions<CloudinarySettings> config)
        {
            var account = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );

            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
        }

        public async Task<ImageUploadResult> UploadDishImageAsync(IFormFile file)
        {
            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "senvie/dishes",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            return await _cloudinary.UploadAsync(uploadParams);
        }
        public async Task<DeletionResult> DeleteImageAsync(string publicId)
        {
            var deleteParams = new DeletionParams(publicId);
            return await _cloudinary.DestroyAsync(deleteParams);
        }
    }
}