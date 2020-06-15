using System.IO;

namespace GprTool
{
    public static class IoExtensions
    {
        public static FileStream OpenReadShared(this string filename)
        {
            return File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public static MemoryStream ReadSharedToStream(this string filename)
        {
            using var fileStream = filename.OpenReadShared();
            var outputStream = new MemoryStream();
            fileStream.CopyTo(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);
            return outputStream;
        }
    }
}
