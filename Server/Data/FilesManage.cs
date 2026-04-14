namespace AuthWithAdmin.Server.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

//לא לגעת - ניהול טוקנים

public class FilesManage
{
    private readonly IWebHostEnvironment _env;

    public FilesManage(IWebHostEnvironment env)
    {
        _env = env;
    }

    public void DeleteFile(string fileName, string containerName)
    {
        string folderPath = Path.Combine(_env.WebRootPath, containerName);

        string savingPath = Path.Combine(folderPath, fileName);

        if (File.Exists(savingPath))
        {
            File.Delete(savingPath);
        }
    }
    
    /// <summary>
    /// Saves any raw file (non-image) by writing bytes directly — no resizing.
    /// Returns the generated stored filename (GUID + original extension).
    /// </summary>
    public async Task<string> SaveRawFile(string fileBase64, string originalFileName, string containerName)
    {
        byte[] fileBytes = Convert.FromBase64String(fileBase64);

        string ext = Path.GetExtension(originalFileName);
        string fileName = string.IsNullOrEmpty(ext)
            ? $"{Guid.NewGuid()}"
            : $"{Guid.NewGuid()}{ext}";

        string folderPath = Path.Combine(_env.WebRootPath, containerName);

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string savingPath = Path.Combine(folderPath, fileName);
        await File.WriteAllBytesAsync(savingPath, fileBytes);
        return fileName;
    }

    public async Task<string> SaveFile(string imageBase64, string extension, string containerName)
    {  
        byte[] picture = Convert.FromBase64String(imageBase64);
        using (Image image = Image.Load(picture))
        {

            image.Mutate(x => x
                .Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(600, 600)
                }));

            var fileName = $"{Guid.NewGuid()}.{extension}";
            string folderPath = Path.Combine(_env.WebRootPath, containerName);

            // Check if the folder exists and create it if it doesn't
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string savingPath = Path.Combine(folderPath, fileName);

            await image.SaveAsync(savingPath); // Automatic encoder selected based on extension.

            return fileName;
        }
    }
    
}