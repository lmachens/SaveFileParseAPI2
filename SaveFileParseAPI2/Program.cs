using Microsoft.AspNetCore.Http;
using System.Runtime;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var count = 0;

app.Use(async (context, next) =>
{
    context.Response.Headers.Add("Access-Control-Allow-Headers", "*");
    context.Response.Headers.Add("Access-Control-Allow-Methods", "*");
    context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
    await next.Invoke();
});


app.MapGet("/", () => "Hello World!");


app.MapPost("/getRawDatabaseImage", async (IFormFile file) =>
{
    var res = Results.Ok();

    res = getImage(file, "RawDatabaseImage");

    // Manage GC
    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    System.GC.Collect();

    return res;
});


app.MapPost("/getRawExclusiveImage", async (IFormFile file) =>
{
    var res = Results.Ok();

    res = getImage(file, "RawExclusiveImage");

    // Manage GC
    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    System.GC.Collect();

    return res;
});


IResult getImage(IFormFile file, String imageName)
{ 
    count++;
    var res = Results.Ok();
    
    var memoryStream = new MemoryStream();
    /*await file.CopyToAsync(memoryStream); */
    file.CopyTo(memoryStream);
    memoryStream.Position = 0;

    // check file size
    if (memoryStream.Length <= 1024)
    {
        res = Results.UnprocessableEntity("Invalid file length.");
    } 
    else
    { 
        // Check that file is a save game file.
        byte[] magic = new byte[4];
        memoryStream.Read(magic, 0, 4);
        if (Encoding.ASCII.GetString(magic) != "GVAS")
        {
            res = Results.UnprocessableEntity("Not an Unreal save file.");
        }
        else
        {
            // Check if file contains imageName
            long pos = memoryStream.Position;
            int rdi = -1;
            using (var streamReader = new StreamReader(memoryStream, Encoding.ASCII, leaveOpen: true))
            {
                string contents = streamReader.ReadToEnd();
                rdi = contents.IndexOf(imageName);

                if (rdi == -1) {
                    return Results.UnprocessableEntity("No "+imageName+" in file.");
                }
            }
            memoryStream.Position = pos; // go back to initial location.

            // Move to the beginning of the Image
            memoryStream.Position += rdi;
            memoryStream.Position += imageName.Length + 1; // Word imageName;
            memoryStream.Position += 4;  // length of ArrayProperty
            memoryStream.Position += 14; // Word ArrayProperty
            memoryStream.Position += 8;  // int64 arraysize  790334=000C0F3E
            memoryStream.Position += 4;  // length of ByteProperty
            memoryStream.Position += 13;  // Word ByteProperty
            memoryStream.Position++;    // padding

            // Get the length of the image
            byte[] tmp = new byte[4];
            memoryStream.ReadExactly(tmp);
            var rdi_length = BitConverter.ToInt32(tmp);

            // Get the image
            byte[]? image = new byte[rdi_length];
            memoryStream.ReadExactly(image);

            // check that image size is correct
            if (image.Length != rdi_length) {
                res = Results.UnprocessableEntity("Bad size.");
            } else {
                // check if image is a SQLite file
                var tag = Encoding.ASCII.GetString(image, 0, 4);
                if (tag == "SQLi")
                {
                    res = Results.Bytes(image, "application/octet-stream", imageName);
                }
                else
                {
                    const ulong PACKAGE_FILE_TAG = 0x9E2A83C1;
                    if (BitConverter.ToUInt64(image[0..8]) != PACKAGE_FILE_TAG)
                    {
                        res = Results.UnprocessableEntity("Not a compressed file.");
                    }
                    else
                    {
                        // Decompress the archive
                        var Ar = new FArchiveLoadCompressedProxy(imageName, image, "Oodle");
                        var uncompressedImage = Ar.ReadArray<byte>();
                        Ar.Dispose();
                        Ar = null;

                        // The bytes store whole FString property including length
                        // Extract the length and skip it in returning bytes
                        var size = BitConverter.ToInt32(uncompressedImage);
                        uncompressedImage = uncompressedImage[4..];

                        // Validate the size since we have that information
                        if (size != uncompressedImage.Length)
                        {
                            res = Results.UnprocessableEntity("Corrupted file");
                        }
                        else
                        {                            
                            res = Results.Bytes(uncompressedImage, "application/octet-stream", imageName);
                        }
                        uncompressedImage = null;
                    } // if (BitConverter.ToUInt64(image[0..8]) != PACKAGE_FILE_TAG)
                } //  if (tag == "SQLi") {
            } // if (image.Length != rdi_length)
            image = null;
        } // if (Encoding.ASCII.GetString(magic) != "GVAS") {
    } //if (memoryStream.Length <= 1024) {


    memoryStream.Close();
    memoryStream.Dispose();
    memoryStream = null;

    // stop app after X call
    int stopCount = 0;
    if (!int.TryParse(Environment.GetEnvironmentVariable("STOPCOUNT"), out stopCount))
    {
        stopCount = 0;
    }
    if (stopCount != 0)
    {
        if (count > stopCount)
        {
            /*await*/ app.StopAsync(TimeSpan.FromSeconds(1));
        }
    }

    return res;
}

app.Run();


